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
exit 0


