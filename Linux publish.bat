set RUNTIME=linux-x64
dotnet publish PacManBot_DotNetCore.csproj --runtime %RUNTIME% --output ".\bin\%RUNTIME%" --configuration Release --self-contained 
robocopy ".\bin\ " ".\bin\%RUNTIME%\ " *.bot /IS
rmdir "bin\Release" /s /q
pause