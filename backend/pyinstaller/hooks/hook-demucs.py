"""
PyInstaller hook for demucs.
Collects all demucs submodules and data files (model configs, etc.).
"""

from PyInstaller.utils.hooks import collect_submodules, collect_data_files

hiddenimports = collect_submodules("demucs")
datas = collect_data_files("demucs")
