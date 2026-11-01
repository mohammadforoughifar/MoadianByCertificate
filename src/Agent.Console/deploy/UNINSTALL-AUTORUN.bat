@echo off
schtasks /delete /tn "InventoryAgent-Boot" /f
schtasks /delete /tn "InventoryAgent-Hourly" /f
schtasks /delete /tn "InventoryAgent-Printers" /f
echo Removed.
pause >nul
