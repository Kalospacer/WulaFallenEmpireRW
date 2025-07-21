@echo off
echo 清理临时文件...
if exist "obj" rmdir /s /q obj
if exist "bin" rmdir /s /q bin
echo 清理完成!