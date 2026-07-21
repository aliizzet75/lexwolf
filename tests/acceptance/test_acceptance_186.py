import os
import pytest

_BASE_HOST = "/docker/openclaw-oo5q/data/.openclaw/workspace-codex/projects/lexwolf"
_BASE = "/data/.openclaw/workspace-codex/projects/lexwolf"
_ROOT = _BASE if os.path.isdir(_BASE) else _BASE_HOST
_DESKTOP = os.path.join(_ROOT, "desktop")


def _read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def test_treeview_in_xaml():
    xaml = _read(os.path.join(_DESKTOP, "MainWindow.xaml"))
    assert "<TreeView" in xaml, "MainWindow.xaml enthält kein <TreeView>"


def test_hierarchical_data_template_in_xaml():
    xaml = _read(os.path.join(_DESKTOP, "MainWindow.xaml"))
    assert "HierarchicalDataTemplate" in xaml, \
        "MainWindow.xaml enthält kein HierarchicalDataTemplate"


def test_file_tree_node_model_exists():
    node_path = os.path.join(_DESKTOP, "Models", "FileTreeNode.cs")
    assert os.path.isfile(node_path), \
        f"Models/FileTreeNode.cs fehlt — ViewModel für TreeView-Knoten nicht vorhanden"


def test_file_tree_node_has_children_and_name():
    node_path = os.path.join(_DESKTOP, "Models", "FileTreeNode.cs")
    content = _read(node_path)
    assert "Children" in content, "FileTreeNode.cs fehlt Children-Property"
    assert "Name" in content, "FileTreeNode.cs fehlt Name-Property"


def test_file_click_handler_in_codebehind():
    cs = _read(os.path.join(_DESKTOP, "MainWindow.xaml.cs"))
    assert "OnFileTreeItemSelected" in cs or "OnFileSelected" in cs or "TreeView_SelectedItem" in cs, \
        "MainWindow.xaml.cs hat keinen Click/Selection-Handler für den Dateibaum"
