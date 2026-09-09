#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
EXAKTNI optimalizace grafu ONNX modelu semanticke segmentace - odstrani vypocet, ktery
na vysledku nic nemeni. Nic se nepretrenovava a nic se nekvantizuje.

Delaji se dve upravy, obe matematicky presne:

  A) **Conv 1x1 za nearest-Resize se presune PRED Resize.** Nearest upsampling jen kopiruje
     pixely a konvoluce 1x1 pracuje pixel po pixelu, takze obe operace KOMUTUJI - vysledek je
     tentyz, ale konvoluce se pocita nad 4x mensim obrazem. V dekoderu je takovych mist sest
     (kazdy stage zacina UpSampling2D a hned za nim je "expand" conv 1x1).

  B) **Dve sousedni Conv 1x1 bez nelinearity mezi nimi se slouci do jedne.**
     y = W2(W1 x + b1) + b2 = (W2 W1) x + (W2 b1 + b2). Takova mista v modelu vznikla tim, ze
     GenericModel20 stavi MobileNetV2 blok BEZ residualniho spojeni a s expansion=1: za
     "project" conv (ta zamerne nema aktivaci - linear bottleneck) nasleduje hned "expand" conv
     dalsiho bloku, taky 1x1. Dve linearni mapy za sebou jsou jedna linearni mapa.

Zisk na Model61.1_float.onnx: **112,5 -> 57,2 MMAC (-49 %)**, na Model96.2_float.onnx
**3 837 -> 2 125 MMAC (-45 %)**, v obou pripadech pri **nezmenenem rozhodnuti na vsech
819 200 pixelech** testovaci sady. Podrobnosti a mereni: doc/semantic-segmentation.md.

⚠️ **Poradi vuci kvantizaci je podstatne.** Slouceni zvetsuje rozsah vah (W2 W1) a odstranuje
jedno mezistupnove zaokrouhleni, takze u int8 / RKNN se presnost muze zmenit oboustranne.
Proto se optimalizuje FLOAT model a kvantizace (onnx2rknn.py) se pousti az na nej - a musi se
preverit merenim, ne predpokladem.

Pouziti:
    python onnxopt.py Model61.1_float.onnx Model61.1_float_opt.onnx --check testset
"""
import argparse
import glob
import os
import sys

import numpy as np
import onnx
from onnx import helper, numpy_helper, shape_inference


def macs(model):
    """Nasobeni s akumulaci na jeden snimek - jedina cena, ktera se tady meri."""
    inferred = shape_inference.infer_shapes(model)
    shapes = {vi.name: [d.dim_value for d in vi.type.tensor_type.shape.dim]
              for vi in (list(inferred.graph.value_info) + list(inferred.graph.input)
                         + list(inferred.graph.output))}
    init = {i.name: numpy_helper.to_array(i) for i in model.graph.initializer}
    total = 0
    for n in model.graph.node:
        if n.op_type != "Conv":
            continue
        w = init.get(n.input[1])
        out = shapes.get(n.output[0], [])
        if w is None or len(out) != 4:
            continue
        total += w.shape[0] * w.shape[1] * w.shape[2] * w.shape[3] * out[2] * out[3]
    return total


def _index(graph):
    init = {i.name: numpy_helper.to_array(i) for i in graph.initializer}
    cons = {}
    for n in graph.node:
        for i in n.input:
            if i:
                cons.setdefault(i, []).append(n)
    return init, cons


def _is_plain_1x1(node, init):
    """Conv 1x1, group=1, stride=1 - jen takova komutuje s resize a slucuje se."""
    if node.op_type != "Conv":
        return False
    w = init.get(node.input[1])
    if w is None or w.shape[2] * w.shape[3] != 1:
        return False
    if next((a.i for a in node.attribute if a.name == "group"), 1) != 1:
        return False
    strides = next((list(a.ints) for a in node.attribute if a.name == "strides"), [1, 1])
    return list(strides) == [1, 1]


def _attrs(node):
    out = {}
    for a in node.attribute:
        if a.ints:
            out[a.name] = list(a.ints)
        elif a.type == onnx.AttributeProto.FLOAT:
            out[a.name] = a.f
        elif a.s:
            out[a.name] = a.s.decode()
        else:
            out[a.name] = a.i
    return out


def optimize(model):
    """Opakuje A) a B), dokud se neco meni. Vraci (pocet presunu, pocet slouceni)."""
    graph = model.graph
    moved = fused = 0
    while True:
        init, cons = _index(graph)
        nodes = list(graph.node)
        changed = False

        # --- A) Conv 1x1 pred nearest-Resize
        for node in nodes:
            if node.op_type != "Resize":
                continue
            after = [x for x in cons.get(node.output[0], []) if x in nodes]
            if len(after) != 1 or not _is_plain_1x1(after[0], init):
                continue
            conv = after[0]
            mid = node.output[0] + "_pre"
            pre = helper.make_node(
                "Conv", [node.input[0], conv.input[1]] + list(conv.input[2:]), [mid],
                name=conv.name + "_pre", kernel_shape=[1, 1], pads=[0, 0, 0, 0],
                strides=[1, 1], dilations=[1, 1], group=1)
            rz_in = list(node.input)
            rz_in[0] = mid
            post = helper.make_node("Resize", rz_in, [conv.output[0]],
                                    name=node.name + "_post", **_attrs(node))
            nodes[nodes.index(node)] = pre
            nodes[nodes.index(conv)] = post
            moved += 1
            changed = True
            break
        if changed:
            del graph.node[:]
            graph.node.extend(nodes)
            continue

        # --- B) slouceni dvou 1x1 bez nelinearity mezi nimi
        init, cons = _index(graph)
        nodes = list(graph.node)
        for idx, node in enumerate(nodes):
            if not _is_plain_1x1(node, init):
                continue
            after = [x for x in cons.get(node.output[0], []) if x in nodes]
            # vic konzumentu = tenzor se jeste pouziva jinde (skip-concat), slucovat nelze
            if len(after) != 1 or not _is_plain_1x1(after[0], init):
                continue
            nxt = after[0]
            w1 = init[node.input[1]]
            b1 = init[node.input[2]] if len(node.input) > 2 else np.zeros(w1.shape[0], np.float32)
            w2 = init[nxt.input[1]]
            b2 = init[nxt.input[2]] if len(nxt.input) > 2 else np.zeros(w2.shape[0], np.float32)
            # float64 zamerne: soucin vah se pocita presneji, nez v cem se pak ulozi
            m1 = w1[:, :, 0, 0].astype(np.float64)
            m2 = w2[:, :, 0, 0].astype(np.float64)
            w = (m2 @ m1).astype(np.float32)[:, :, None, None]
            b = (m2 @ b1.astype(np.float64) + b2.astype(np.float64)).astype(np.float32)
            wn, bn = nxt.name + "_W_fused", nxt.name + "_B_fused"
            graph.initializer.append(numpy_helper.from_array(w, wn))
            graph.initializer.append(numpy_helper.from_array(b, bn))
            merged = helper.make_node("Conv", [node.input[0], wn, bn], [nxt.output[0]],
                                      name=nxt.name + "_fused", kernel_shape=[1, 1],
                                      pads=[0, 0, 0, 0], strides=[1, 1], dilations=[1, 1], group=1)
            nodes[idx] = merged
            nodes.remove(nxt)
            fused += 1
            changed = True
            break
        if not changed:
            break
        del graph.node[:]
        graph.node.extend(nodes)

    used = {i for n in graph.node for i in n.input}
    keep = [i for i in graph.initializer if i.name in used]
    del graph.initializer[:]
    graph.initializer.extend(keep)
    del graph.value_info[:]
    return moved, fused


def check(src, dst, truth_dir):
    """Overi, ze se NEZMENILO ROZHODNUTI - ne ze se nezmenila cisla (float ma svuj sum)."""
    try:
        import onnxruntime as ort
        from PIL import Image
    except ImportError as e:
        print("  kontrola preskocena: %s" % e)
        return
    imgs = sorted(glob.glob(os.path.join(truth_dir, "img", "*.jpg")))
    if not imgs:
        imgs = sorted(glob.glob(os.path.join(truth_dir, "*.jpg")))
    if not imgs:
        print("  kontrola preskocena: v %s nejsou obrazky" % truth_dir)
        return
    so = ort.SessionOptions()
    so.log_severity_level = 3
    so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_DISABLE_ALL
    a = ort.InferenceSession(src, so, providers=["CPUExecutionProvider"])
    b = ort.InferenceSession(dst, so, providers=["CPUExecutionProvider"])
    na, nb = a.get_inputs()[0].name, b.get_inputs()[0].name
    h, w = a.get_inputs()[0].shape[1:3]
    worst, diff, total = 0.0, 0, 0
    for f in imgs:
        x = (np.asarray(Image.open(f).convert("RGB").resize((w, h), Image.NEAREST),
                        np.float32) / 255.0)[None]
        ya = a.run(None, {na: x})[0]
        yb = b.run(None, {nb: x})[0]
        worst = max(worst, float(np.abs(ya - yb).max()))
        diff += int(((ya[..., 1] > ya[..., 0]) != (yb[..., 1] > yb[..., 0])).sum())
        total += ya[..., 0].size
    print("  kontrola na %d snimcich: max |rozdil pravdepodobnosti| %.2e" % (len(imgs), worst))
    print("  pixelu s JINYM rozhodnutim: %d z %d%s"
          % (diff, total, "" if diff else "   <- tohle musi byt 0"))
    if diff:
        sys.exit("Optimalizace zmenila rozhodnuti - to je chyba, ne kompromis.")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", help="vstupni .onnx (FLOAT model)")
    ap.add_argument("dst", help="vystupni .onnx")
    ap.add_argument("--check", metavar="DIR",
                    help="adresar testovaci sady (models/testset) - overi shodu rozhodnuti")
    args = ap.parse_args()

    model = onnx.load(args.src)
    before_nodes, before_macs = len(model.graph.node), macs(model)
    print("vstup:  %d uzlu, %.1f MMAC, %.2f MB"
          % (before_nodes, before_macs / 1e6, os.path.getsize(args.src) / 1e6))

    moved, fused = optimize(model)
    model = shape_inference.infer_shapes(model)
    onnx.checker.check_model(model)
    onnx.save(model, args.dst)

    print("  presunuto Conv 1x1 pred Resize: %d" % moved)
    print("  slouceno paru Conv 1x1:         %d" % fused)
    after_macs = macs(model)
    print("vystup: %d uzlu, %.1f MMAC, %.2f MB"
          % (len(model.graph.node), after_macs / 1e6, os.path.getsize(args.dst) / 1e6))
    print("uspora: %.1f MMAC = %.1f %%"
          % ((before_macs - after_macs) / 1e6, 100.0 * (before_macs - after_macs) / before_macs))

    if args.check:
        check(args.src, args.dst, args.check)


if __name__ == "__main__":
    main()
