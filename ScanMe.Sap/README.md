<!--
SharePoint-Analyse im ScanMe-Fork:
- Settings liegen in `NAPS2.Lib/Scan/ScanProfile.cs`: `EnableSharePointUpload` plus `SharePointUploadSettings`.
- `SharePointUploadSettings` enthält SiteUrl, LibraryNameOrPath, FolderPath, TenantId, ClientId und ClientSecret.
- Der Upload wird nach erfolgreichem AutoSave-PDF in `NAPS2.Lib/ImportExport/AutoSaver.SaveOneFile` getriggert.
- Hook: direkt nach `SavePdfOperation`/`op.Success`, nur bei `ActiveProfile.EnableSharePointUpload == true`.
- Ausgeführt wird `UploadSharePointOperation`, das `SharePointUploadService.UploadFileAsync` aufruft.
- Die UI sitzt in `NAPS2.Lib/EtoForms/Ui/EditProfileForm.cs` im GroupBox-Block "SharePoint Upload".
- Credentials werden aktuell nicht verschlüsselt persistiert: `ClientSecret` steht als Klartext im XML-Profil.
- Eine DPAPI/ProtectedData-Wiederverwendung für SharePoint wurde in den untersuchten Projekten nicht gefunden.
-->
