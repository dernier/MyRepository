"""Genera tiny_yolo.onnx: finto modello YOLOv8 (ingresso [1,3,64,64], uscita [1,84,3]) per i test.

L'uscita è costante: colonna 0 = persona al centro (cx=32, cy=32, 20x20, 0.9),
colonna 1 = bottiglia (cx=10, cy=50, 8x8, 0.8), colonna 2 = nessun oggetto.
Uso: pip install onnx && python make_tiny_yolo.py
"""
import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

out = np.zeros((1, 84, 3), dtype=np.float32)
out[0, 0:4, 0] = [32, 32, 20, 20]
out[0, 4 + 0, 0] = 0.9   # person
out[0, 0:4, 1] = [10, 50, 8, 8]
out[0, 4 + 39, 1] = 0.8  # bottle

nodes = [
    helper.make_node("ReduceMean", ["images"], ["mean"], axes=[1, 2, 3], keepdims=0),
    helper.make_node("Mul", ["mean", "zero"], ["nothing"]),
    helper.make_node("Add", ["const", "nothing"], ["output0"]),
]
graph = helper.make_graph(
    nodes, "tiny_yolo",
    [helper.make_tensor_value_info("images", TensorProto.FLOAT, [1, 3, 64, 64])],
    [helper.make_tensor_value_info("output0", TensorProto.FLOAT, [1, 84, 3])],
    initializer=[numpy_helper.from_array(out, "const"), numpy_helper.from_array(np.zeros(1, np.float32), "zero")],
)
model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)])
model.ir_version = 8
onnx.checker.check_model(model)
onnx.save(model, "tiny_yolo.onnx")
