#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Export Keras checkpointu (.h5) do ONNX s PEVNYMI tvary - float, bez kvantizace.

Proc to existuje vedle tflite2onnx.py: soubory `.tflite` v models/ jsou vyrobene
`Optimize.DEFAULT` bez representative_dataset, tedy **dynamic-range kvantizaci** - vahy int8,
pocita se ve floatu. Pro CPU cestu to nevadi (prevede se, jak je), ale RKNN takovy model odmitne
kvantizovat ("If a quantized model has been import, please set do_quantization = False"), takze
by na NPU zbyl jen float16. Skutecne float vahy jsou uz jen v Keras checkpointu - odtud tenhle
export. Viz doc/semantic-segmentation.md.

Pevny tvar davky je podminka pro RKNN i pro predalokovane buffery v OnnxBackProject; Keras
export bez nej necha davku dynamickou ('?').

Pouziti (Linux / WSL, Python 3.11 s TensorFlow):
    .venv/bin/python keras2onnx.py Model96.2_0.9643115401268005.h5 Model96.2_float.onnx
"""
import argparse
import os
import sys


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("h5", help="vstupni Keras checkpoint (.h5)")
    ap.add_argument("onnx", help="vystupni .onnx")
    ap.add_argument("--opset", type=int, default=13)
    ap.add_argument("--size", type=int, default=0,
                    help="hrana vstupu [px]; 0 = vzit z modelu")
    a = ap.parse_args()

    import tensorflow as tf
    import tf2onnx

    print("Nacitam %s" % a.h5)
    # compile=False: checkpoint muze nest vlastni loss/metriku, kterou tu stejne nepotrebujeme
    # a jejiz chybejici definice by nacteni shodila.
    model = tf.keras.models.load_model(a.h5, compile=False)

    shape = model.input_shape          # (None, H, W, C)
    h = a.size or shape[1]
    w = a.size or shape[2]
    c = shape[3]
    if not h or not w or not c:
        sys.exit("Model ma neurcity vstupni tvar %s - zadej --size." % (shape,))
    print("  vstup modelu: %s -> pevne (1, %d, %d, %d)" % (shape, h, w, c))
    print("  vystup modelu: %s" % (model.output_shape,))

    spec = (tf.TensorSpec((1, h, w, c), tf.float32, name="input"),)
    tf2onnx.convert.from_keras(model, input_signature=spec, opset=a.opset, output_path=a.onnx)

    # Kontrola, ze tvary opravdu vysly staticke - tvrdit to bez overeni by bylo k nicemu.
    import onnx
    m = onnx.load(a.onnx)
    for kind, vals in (("vstup", m.graph.input), ("vystup", m.graph.output)):
        for v in vals:
            dims = [d.dim_value if d.HasField("dim_value") else "?"
                    for d in v.type.tensor_type.shape.dim]
            print("  %-7s %-28s %s" % (kind, v.name, dims))
            if any(d == "?" or d == 0 for d in dims):
                print("    ⚠️ POZOR: rozmer neni staticky - RKNN takovy model neprevede.")
    print("Hotovo: %s (%.0f kB)" % (a.onnx, os.path.getsize(a.onnx) / 1024.0))


if __name__ == "__main__":
    main()
