cctk.exe --setuppwd=REMOVED_SECRET
cctk.exe --valsetuppwd=REMOVED_SECRET bootorder --activebootlist=uefi
cctk.exe --valsetuppwd=REMOVED_SECRET --AcPwrRcvry=on
cctk.exe --valsetuppwd=REMOVED_SECRET --AdminSetupLockout=enable
cctk.exe --valsetuppwd=REMOVED_SECRET --LegacyOrom=disable
cctk.exe --valsetuppwd=REMOVED_SECRET --SecureBoot=enable
cctk.exe --valsetuppwd=REMOVED_SECRET --TpmSecurity=on
cctk.exe --valsetuppwd=REMOVED_SECRET --TpmActivation=Enabled
cctk.exe --valsetuppwd=REMOVED_SECRET --UefiBootPathSecurity=alwaysexceptinternalhdd
cctk.exe --valsetuppwd=REMOVED_SECRET --UefiNwStack=enable
cctk.exe --valsetuppwd=REMOVED_SECRET --WakeOnLan=enablewakeonwlan
exit 0


