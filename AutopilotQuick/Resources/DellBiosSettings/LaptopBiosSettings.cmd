cctk.exe --setuppwd=PCSD202
cctk.exe --valsetuppwd=PCSD202 bootorder --activebootlist=uefi
cctk.exe --valsetuppwd=PCSD202 --AcPwrRcvry=on
cctk.exe --valsetuppwd=PCSD202 --AdminSetupLockout=enable
cctk.exe --valsetuppwd=PCSD202 --LegacyOrom=disable
cctk.exe --valsetuppwd=PCSD202 --SecureBoot=enable
cctk.exe --valsetuppwd=PCSD202 --TpmSecurity=on
cctk.exe --valsetuppwd=PCSD202 --TpmActivation=Enabled
cctk.exe --valsetuppwd=PCSD202 --UefiBootPathSecurity=alwaysexceptinternalhdd
cctk.exe --valsetuppwd=PCSD202 --UefiNwStack=enable
cctk.exe --valsetuppwd=PCSD202 --WakeOnLan=enablewakeonwlan
cctk.exe --valsetuppwd=PCSD202 --AlwaysAllowDellDocks=Enabled
cctk.exe --valsetuppwd=PCSD202 --BrightnessAc=15
cctk.exe --valsetuppwd=PCSD202 --BrightnessBattery=9
cctk.exe --valsetuppwd=PCSD202 --Fastboot=Thorough
cctk.exe --valsetuppwd=PCSD202 --PowerWarn=Disabled
cctk.exe --valsetuppwd=PCSD202 --WakeOnDock=Enabled
cctk.exe --valsetuppwd=PCSD202 --WarningsAndErr=PromptWrnErr
cctk.exe --valsetuppwd=PCSD202 --WirelessLan=Enabled
exit 0
