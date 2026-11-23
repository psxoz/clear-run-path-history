# ClearRunPathHistory

Утилита для Windows очищает историю локальных путей, которые появляются при вводе `/` в классическом окне «Выполнить» (`Win+R`). Она удаляет записи истории через системный API. Сами файлы остаются на диске.

## Скачать

Скачайте `ClearRunPathHistory.exe` из [последнего релиза](../../releases/latest) и запустите его. Очистка выполняется сразу при запуске. После успешной очистки программа закрывается.

Программа проверена на Windows 10. Запуск на Windows 11 пока не проверен.

## Собрать из исходников

В PowerShell из папки с `ClearRunPathHistory.cs` выполните:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:winexe /platform:anycpu /reference:System.Windows.Forms.dll /out:ClearRunPathHistory.exe ClearRunPathHistory.cs
```
