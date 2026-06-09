$b = [IO.File]::ReadAllBytes("$PSScriptRoot\..\src\Lhamiel\Resources\Locales\en_US.axaml")
"{0:X2} {1:X2} {2:X2}" -f $b[0], $b[1], $b[2]
