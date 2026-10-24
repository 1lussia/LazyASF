@echo off
chcp 65001 >nul
:: Разрешает телефону достучаться до LazyASF (TCP 47800 + UDP 47801) в частной сети.
:: Запускать правой кнопкой -> "Запуск от имени администратора".
netsh advfirewall firewall delete rule name="LazyASF TCP" >nul 2>&1
netsh advfirewall firewall delete rule name="LazyASF UDP" >nul 2>&1
netsh advfirewall firewall add rule name="LazyASF TCP" dir=in action=allow protocol=TCP localport=47800 profile=private,domain
netsh advfirewall firewall add rule name="LazyASF UDP" dir=in action=allow protocol=UDP localport=47801 profile=private,domain
echo.
echo Готово. Если домашняя Wi-Fi сеть помечена в Windows как "Общедоступная" -
echo переключи её на "Частная" (Параметры - Сеть - Wi-Fi - свойства сети).
pause
