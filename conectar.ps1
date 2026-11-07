$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb -s emulator-5554 shell svc wifi enable
& $adb -s emulator-5554 shell svc data enable