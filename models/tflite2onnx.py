#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Prevod modelu semanticke segmentace sjizdnosti z TFLite do ONNX pro ARBot3.

Proc: ARBot3 dela inferenci pres ONNX Runtime (NuGet Microsoft.ML.OnnxRuntime), ktery ma
nativni knihovnu pro win-x64 i linux-arm64 - tedy TENTYZ kod v simulaci na Windows i na
Orange Pi. TFLite runtime by znamenal vlastni nativni knihovnu na obe platformy.
Viz doc/semantic-segmentation.md.

Co skript dela navic proti holemu tf2onnx: **prepise vstup a vystup modelu na float32**.
tf2onnx necha u plne kvantovaneho modelu I/O jako uint8, takze by volajici musel znat
kvantizacni konstanty (scale/zero_point) a kvantizovat sam - presne to delal ARBot2
v EdgeTPUDll/EdgeTPU.cpp. Kdyz se misto toho zahodi uvodni DequantizeLinear a zaverecny
QuantizeLinear, model prijima rovnou realne hodnoty (vstup 0..1, vystup pravdepodobnost)
a vnitrek zustava int8. C# strana pak nema zadne magicke konstanty: v/255 dovnitr,
pravdepodobnost ven.

Pouziti (Linux / WSL, Python 3.11):
    python -m venv .venv && .venv/bin/pip install tensorflow-cpu==2.15.1 tf2onnx==1.16.1 onnx==1.16.1
    .venv/bin/python tflite2onnx.py Model61.1_int8.tflite Model61.1_int8.onnx

Overeni proti TFLite (vyzaduje jeste onnxruntime):
    .venv/bin/python tflite2onnx.py Model61.1_int8.tflite Model61.1_int8.onnx --check obrazek.png
"""
import argparse
import subprocess
import sys

import numpy as np
import onnx


def convert(tflite_path, onnx_path, opset, dequantize):
    """Zavola tf2onnx. --dequantize rozbali vahy do float (vetsi soubor, float vnitrek)."""
    cmd = [sys.executable, "-m", "tf2onnx.convert", "--tflite", tflite_path,
           "--output", onnx_path, "--opset", str(opset)]
    if dequantize:
        cmd.append("--dequantize")
    print("  " + " ".join(cmd))
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL)


_PASSTHROUGH = ("Transpose", "Identity", "Reshape", "Squeeze", "Unsqueeze")


def _producer(g, name):
    for n in g.node:
        if name in n.output:
            return n
    return None


def _consumer_chain_from(g, name):
    """Uzel, ktery jmeno spotrebuje; preskoci pretvarovaci uzly (tf2onnx sype Transpose NCHW<->NHWC)."""
    for n in g.node:
        if n.input and n.input[0] == name:
            if n.op_type in _PASSTHROUGH:
                return _consumer_chain_from(g, n.output[0])
            return n
    return None


def _producer_chain_of(g, name):
    """Uzel, ktery jmeno vyrobil; preskoci pretvarovaci uzly smerem dozadu."""
    n = _producer(g, name)
    while n is not None and n.op_type in _PASSTHROUGH:
        n = _producer(g, n.input[0])
    return n


def unwrap_io(onnx_path):
    """
    Prepise I/O modelu na float32: zahodi uvodni DequantizeLinear a zaverecny QuantizeLinear.
    Vnitrek modelu zustava int8 - meni se jen to, v cem se mluvi na hranici.

    Vraci (vstupni_scale, vstupni_zero_point) jen pro vypis - volajici je uz nepotrebuje.
    """
    model = onnx.load(onnx_path)
    g = model.graph
    inits = {i.name: onnx.numpy_helper.to_array(i) for i in g.initializer}
    changed = []
    qin = None

    # Vstup: input(uint8) -> [Transpose] -> DequantizeLinear -> x(float). Vstupem se stane x.
    # S --dequantize je tataz operace rozepsana na Cast -> Sub -> Mul, proto dva vzory.
    n = _consumer_chain_from(g, g.input[0].name)
    if n is not None and n.op_type == "DequantizeLinear":
        sc, zp = inits.get(n.input[1]), inits.get(n.input[2]) if len(n.input) > 2 else 0
        qin = (float(sc), int(zp))
        # Spotrebitele vystupu Dequantize prepojit na jeho vstup a uzel zahodit.
        _rewire(g, n.output[0], n.input[0])
        g.input[0].type.tensor_type.elem_type = onnx.TensorProto.FLOAT
        g.node.remove(n)
        changed.append("vstup -> float32")
    elif n is not None and n.op_type == "Cast":
        chain, cur, zp, sc = [n], n.output[0], 0.0, None
        for op in ("Sub", "Mul"):
            nx = _consumer_chain_from(g, cur)
            if nx is None or nx.op_type != op:
                chain = None
                break
            val = inits.get(nx.input[1])
            if op == "Sub":
                zp = float(val) if val is not None else 0.0
            else:
                sc = float(val) if val is not None else None
            chain.append(nx)
            cur = nx.output[0]
        if chain and sc is not None:
            qin = (sc, int(zp))
            _rewire(g, cur, g.input[0].name)
            g.input[0].type.tensor_type.elem_type = onnx.TensorProto.FLOAT
            for x in chain:
                g.node.remove(x)
            changed.append("vstup -> float32")

    # Vystup: y(float) -> QuantizeLinear -> [Transpose] -> Identity(uint8). Vystupem se stane y.
    n = _producer_chain_of(g, g.output[0].name)
    if n is not None and n.op_type == "QuantizeLinear":
        _rewire(g, n.output[0], n.input[0])
        if g.output[0].name == n.output[0]:
            g.output[0].name = n.input[0]
        g.output[0].type.tensor_type.elem_type = onnx.TensorProto.FLOAT
        g.node.remove(n)
        changed.append("vystup -> float32")

    if changed:
        onnx.checker.check_model(model)
        onnx.save(model, onnx_path)
    print("  uprava I/O: " + (", ".join(changed) if changed else "nic (I/O uz nebylo kvantovane)"))
    return qin


def _rewire(g, old_name, new_name):
    """Vsechny vstupy uzlu, ktere braly old_name, prepoji na new_name."""
    for m in g.node:
        for i, inp in enumerate(m.input):
            if inp == old_name:
                m.input[i] = new_name


def describe(onnx_path):
    m = onnx.load(onnx_path)
    for kind, vals in (("vstup", m.graph.input), ("vystup", m.graph.output)):
        for v in vals:
            t = v.type.tensor_type
            shape = [d.dim_value if d.HasField("dim_value") else "?" for d in t.shape.dim]
            print("  %-7s %-10s %-22s %s" % (kind, v.name, shape,
                                             onnx.TensorProto.DataType.Name(t.elem_type)))


def load_image(path, w, h):
    """Nacte obrazek a zmensi ho nejblizsim sousedem - stejne jako Image<T>.Resize v ARBot3."""
    import tensorflow as tf
    img = tf.io.decode_image(tf.io.read_file(path), channels=3).numpy()
    ih, iw, _ = img.shape
    ys = np.arange(h) * ih // h
    xs = np.arange(w) * iw // w
    return img[np.ix_(ys, xs)].astype(np.uint8)[None, ...]


def check(tflite_path, onnx_path, image):
    """
    Porovna ONNX proti TFLite na tomtez vstupu. TFLite je zdroj pravdy - je to model,
    ktery jel v ARBot2. Predzpracovani odpovida EdgeTPUDll/EdgeTPU.cpp: RGB, hodnota/255.
    """
    import tensorflow as tf
    import onnxruntime as ort

    it = tf.lite.Interpreter(model_path=tflite_path)
    it.allocate_tensors()
    di, do = it.get_input_details()[0], it.get_output_details()[0]
    _, h, w, _ = di["shape"]
    isc, izp = di["quantization"]
    osc, ozp = do["quantization"]

    rng = np.random.default_rng(1)
    cases = [("sum", rng.integers(0, 255, (1, h, w, 3), dtype=np.uint8))]
    if image:
        cases.append(("obrazek", load_image(image, w, h)))

    s = ort.InferenceSession(onnx_path, providers=["CPUExecutionProvider"])
    iname = s.get_inputs()[0].name
    ifloat = "float" in s.get_inputs()[0].type    # vstup uz je v realnych jednotkach (0..1)
    ofloat = "float" in s.get_outputs()[0].type   # vystup uz je pravdepodobnost

    for label, x in cases:
        # TFLite: kvantizace vstupu presne jako ARBot2 (v/255 -> q)
        q = np.clip(np.round((x / 255.0) / isc) + izp, 0, 255).astype(np.uint8)
        it.set_tensor(di["index"], q)
        it.invoke()
        ref = (it.get_tensor(do["index"]).astype(np.float32) - ozp) * osc

        y = s.run(None, {iname: (x / 255.0).astype(np.float32) if ifloat else q})[0].astype(np.float32)
        if not ofloat:
            y = (y - ozp) * osc

        d = np.abs(y - ref)
        agree = (np.argmax(y, -1) == np.argmax(ref, -1)).mean() if y.shape[-1] > 1 else \
                ((y > 0.5) == (ref > 0.5)).mean()
        print("  %-8s max|diff| %.4f  mean|diff| %.5f  shoda rozhodnuti %.2f %%"
              % (label, d.max(), d.mean(), 100 * agree))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("tflite", help="vstupni .tflite (plne kvantovany, staticke tvary)")
    ap.add_argument("onnx", help="vystupni .onnx")
    ap.add_argument("--opset", type=int, default=13)
    ap.add_argument("--dequantize", action="store_true",
                    help="rozbalit vahy do float32 (vetsi model, float vnitrek) - pro A/B mereni")
    ap.add_argument("--keep-quantized-io", action="store_true",
                    help="nechat I/O uint8 tak, jak ho da tf2onnx (volajici pak musi kvantizovat sam)")
    ap.add_argument("--check", metavar="OBRAZEK", nargs="?", const="",
                    help="po prevodu porovnat s TFLite (volitelne na dodanem obrazku)")
    a = ap.parse_args()

    print("Prevod %s -> %s" % (a.tflite, a.onnx))
    convert(a.tflite, a.onnx, a.opset, a.dequantize)
    if not a.keep_quantized_io:
        qin = unwrap_io(a.onnx)
        if qin:
            print("  (vstupni kvantizace modelu byla scale=%.8f zero_point=%d - ted uz ji resi model sam)" % qin)
    describe(a.onnx)
    if a.check is not None:
        print("Overeni proti TFLite:")
        check(a.tflite, a.onnx, a.check or None)


if __name__ == "__main__":
    main()
