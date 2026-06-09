!macro customUnInit
  ; Kill the process if it's running
  nsExec::Exec 'taskkill /F /IM "Scan Citizen ID Card.exe" /T'
  
  ; Remove the auto-launch registry key if it exists
  ; The key name defaults to the app name or productName
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Scan Citizen ID Card"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "cccd-scanner"
!macroend
