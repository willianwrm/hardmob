# hardmob
Proxy entre novas publicações de promoções do hardmob com bot de telegram

# Install from code
1 - git clone

2 - open the solution file 'Hardmob.sln' in VisualStudio

3 - compile it

4 - copy the 'config-sample.ini' to 'bin\Debug\config.exe' and edit 'config.ini' with your [bot Token](https://core.telegram.org/bots#how-do-i-create-a-bot) and [chat ID](https://stackoverflow.com/questions/32423837/telegram-bot-how-to-get-a-group-chat-id)

5 - install as a service using [InstallUtil](https://learn.microsoft.com/dotnet/framework/tools/installutil-exe-installer-tool) from .NET Framework, example in cmd:
```
cd %WINDIR%\Microsoft.NET\Framework64\v4.0.30319
InstallUtil.exe "C:\HardMob\Hardmob.exe"
```

6 - you can now start it as a [service](https://learn.microsoft.com/dotnet/framework/windows-services/introduction-to-windows-service-applications)
