@echo off
REM Launch the s&box editor with third-party Vulkan overlay layers switched off.
REM
REM Five implicit layers inject themselves into the editor process (Overwolf x2, OBS hook,
REM EOS Overlay, Steam Overlay, Rockstar Social Club) and several are built against older
REM Vulkan versions than the engine asks for -- the engine logs a warning about each one at
REM startup. They wrap every Vulkan call, so they are the first thing to rule out whenever a
REM crash looks like GPU memory corruption rather than a C# bug.
REM
REM "~implicit~" disables all implicit layers for THIS PROCESS ONLY. Nothing is uninstalled and
REM no registry key is touched: close this and launch normally and every overlay is back.
REM Requires Vulkan loader 1.3.234+ (the installed NVIDIA 610.x driver is far newer).

set VK_LOADER_LAYERS_DISABLE=~implicit~

cd /d "c:\Program Files (x86)\Steam\steamapps\common\sbox"
start "" sbox-dev.exe
