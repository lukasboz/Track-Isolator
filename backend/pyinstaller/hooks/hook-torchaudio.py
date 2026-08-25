"""
PyInstaller hook for torchaudio.
Collects backend libraries (.so/.dll/.dylib) and submodules.
"""

from PyInstaller.utils.hooks import collect_submodules, collect_dynamic_libs, collect_data_files

hiddenimports = collect_submodules("torchaudio")
binaries = collect_dynamic_libs("torchaudio")
datas = collect_data_files("torchaudio")
