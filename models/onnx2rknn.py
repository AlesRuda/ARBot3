#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Prevod modelu semanticke segmentace z ONNX do RKNN (NPU na RK3588 / Orange Pi 5 Ultra).

Navazuje na tflite2onnx.py: ten vyrobi ONNX pro CPU cestu (OnnxBackProject), tenhle z nej
udela .rknn pro NPU cestu (RknnBackProject). Viz doc/semantic-segmentation.md.

**Zdrojem je FLOAT model** (Model61.1_float.onnx), ne kvantovany. RKNN ma vlastni kvantizacni
schema a kvantizuje si sam z kalibracni sady; nacist uz kvantovany model by znamenalo kvantizovat
dvakrat.

**Normalizaci dela NPU.** rknn.config(mean_values=0, std_values=255) znamena, ze model dostane
syrove uint8 pixely 0..255 a (x - 0) / 255 si udela sam - tedy tentyz kontrakt jako u ONNX cesty
(vstup 0..1), jen se to pocita na NPU misto v C#. Volajici proto posila prosty BGR32 -> RGB uint8
buffer bez deleni.

⚠️ **Kalibracni sada je tataz, na ktere se pak meri presnost** (models/testset) - jinou oznackovanou
sadu nemame. Presnost RKNN modelu je tim merena OPTIMISTICKY; cilem tehle cesty je cas, ne presnost.

Pouziti (Linux / WSL, Python 3.8-3.12):
    python -m venv .venv && .venv/bin/pip install rknn-toolkit2
    .venv/bin/python onnx2rknn.py Model61.1_float.onnx Model61.1.rknn --dataset testset/img
"""
import argparse
import glob
import os
import sys
import tempfile


def build_dataset(img_dir, limit):
    """RKNN chce textovy soubor s jednou cestou k obrazku na radek."""
    imgs = sorted(glob.glob(os.path.join(img_dir, "*.jpg")) + glob.glob(os.path.join(img_dir, "*.png")))
    if not imgs:
        sys.exit("V %s nejsou zadne obrazky pro kalibraci." % img_dir)
    if limit:
        imgs = imgs[:limit]
    fd, path = tempfile.mkstemp(suffix=".txt", prefix="rknn-dataset-")
    with os.fdopen(fd, "w") as f:
        for p in imgs:
            f.write(os.path.abspath(p) + "\n")
    print("  kalibracni sada: %d obrazku (%s)" % (len(imgs), path))
    return path


def nchw_if_needed(path):
    """
    RKNN umi u ONNX jen **NCHW** vstup. Nas model je NHWC (pochazi z TFLite) a RKNN to hlasi
    matouci hlaskou: "The len of mean_values ([0,0,0]) for input 0 is wrong, expect 128!" —
    bere si totiz vysku za pocet kanalu.

    Lecba: pred vstup se vlozi Transpose NCHW->NHWC a vstup grafu se prepise na NCHW. Vnitrek
    modelu ani vystup se nemeni (vystup zustava NHWC, takze se na strane C# nic neprepocitava)
    a **volajici posila porad NHWC** — runtime si prerovnani udela sam podle rknn_input.fmt.
    Vraci cestu k pouzitemu modelu (puvodni, kdyz uz NCHW je).
    """
    import onnx
    from onnx import helper, TensorProto

    m = onnx.load(path)
    g = m.graph
    dims = [d.dim_value for d in g.input[0].type.tensor_type.shape.dim]
    if len(dims) != 4:
        return path
    # Dynamicka davka (dim_value == 0, tedy "?") se PEVNE nastavi na 1. RKNN dynamicke tvary
    # neumi a bez tohohle by vznikl model s davkou 0 - tedy nic. Aplikace stejne pousti
    # snimky po jednom.
    if dims[0] <= 0:
        dims[0] = 1
    if dims[1] <= 4 and dims[3] > 4:
        return path                      # uz je NCHW
    if dims[3] > 4:
        sys.exit("Vstup %s ma tvar %s - neni to ani NCHW, ani NHWC s 1-4 kanaly." % (path, dims))

    n, h, w, c = dims
    old = g.input[0].name
    new = old + "_nchw"
    g.node.insert(0, helper.make_node("Transpose", [new], [old], perm=[0, 2, 3, 1], name="nhwc_in"))
    g.input.remove(g.input[0])
    g.input.insert(0, helper.make_tensor_value_info(new, TensorProto.FLOAT, [n, c, h, w]))
    onnx.checker.check_model(m)

    # Docasny soubor: prepsany model je jen mezikrok k .rknn, v repu by jen matl.
    fd, out = tempfile.mkstemp(suffix="-nchw.onnx", prefix="rknn-")
    os.close(fd)
    onnx.save(m, out)
    print("  vstup prepsan z NHWC %s na NCHW %s -> %s" % (dims, [n, c, h, w], os.path.basename(out)))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("onnx", help="vstupni .onnx (float, PEVNE tvary)")
    ap.add_argument("rknn", help="vystupni .rknn")
    ap.add_argument("--dataset", default="testset/img", help="adresar s kalibracnimi obrazky")
    ap.add_argument("--limit", type=int, default=0, help="kolik obrazku pouzit (0 = vsechny)")
    ap.add_argument("--platform", default="rk3588")
    ap.add_argument("--optlevel", type=int, default=3,
                    help="optimalizacni uroven RKNN (0-3). Nizsi = mene fuzi, ktere umi ublizit "
                         "presnosti kvantizace; RKNN sam doporucuje 2, kdyz si vsimne QAT modelu")
    ap.add_argument("--no-quant", action="store_true",
                    help="nekvantizovat (float16 na NPU) - pro A/B presnosti proti kvantizovanemu")
    a = ap.parse_args()

    from rknn.api import RKNN

    rknn = RKNN(verbose=False)

    # mean/std: NPU dostane uint8 0..255 a udela (x-0)/255 -> model vidi 0..1, jak byl trenovan.
    print("Konfigurace pro %s" % a.platform)
    rknn.config(mean_values=[[0, 0, 0]], std_values=[[255, 255, 255]],
                target_platform=a.platform, optimization_level=a.optlevel)

    print("Nacitam %s" % a.onnx)
    docasny = None
    if a.onnx.lower().endswith(".tflite"):
        # ⚠️ KVANTOVANY tflite tudy NECHOD: RKNN u nej hlasi "std_values are ignored" a odmitne
        # do_quantization - vstup pak chce uz kvantovany, tedy s magickymi konstantami na strane
        # C#. Prave tomu se cela cesta vyhyba (viz doc/semantic-segmentation.md).
        rc = rknn.load_tflite(model=a.onnx)
    else:
        model = nchw_if_needed(a.onnx)
        docasny = model if model != a.onnx else None
        rc = rknn.load_onnx(model=model)
    if rc != 0:
        sys.exit("nacteni modelu selhalo")

    if a.no_quant:
        print("Sestavuji BEZ kvantizace (float16)")
        ok = rknn.build(do_quantization=False)
    else:
        ds = build_dataset(a.dataset, a.limit)
        print("Sestavuji s kvantizaci")
        ok = rknn.build(do_quantization=True, dataset=ds)
        os.unlink(ds)
    if ok != 0:
        sys.exit("build selhalo")

    if rknn.export_rknn(a.rknn) != 0:
        sys.exit("export_rknn selhalo")
    if docasny and os.path.exists(docasny):
        os.unlink(docasny)
    print("Hotovo: %s (%.0f kB)" % (a.rknn, os.path.getsize(a.rknn) / 1024.0))
    rknn.release()


if __name__ == "__main__":
    main()
