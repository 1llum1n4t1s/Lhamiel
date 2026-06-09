# CodeRabbit PR #59 レビュー指摘対応:
# 全 15 ロケールの新規パスワード保護キーを各言語に翻訳する。
# 英訳ブロックを言語別翻訳済みブロックに正確に置換。

$ErrorActionPreference = 'Stop'

$localesDir = "$PSScriptRoot\..\src\Lhamiel\Resources\Locales"

# 各ロケールの「コメント (Comment)」と 29 キーの翻訳辞書。
# キー = ロケール名、値 = ハッシュテーブル (Comment + 29 翻訳)。
$translations = @{

    'zh_CN' = @{
        Comment = '密码保护 (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = '密码保护'
            'Text.Settings.Compression.EnablePassword' = '使用密码保护'
            'Text.Settings.Compression.EnablePasswordDescription' = 'ZIP 使用 AES-256 (WinZip AE-2) 加密,7z 使用 AES-256 加密。'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR 格式不支持密码保护。请选择 ZIP 或 7z。'
            'Text.Settings.Compression.ZipAesExplorerNote' = '注意:AES-256 加密的 ZIP 无法在 Windows 自带的资源管理器中解压。收件人需要 7-Zip、WinRAR 等兼容工具。'
            'Text.Settings.Compression.EncryptFileNames' = '同时加密文件名'
            'Text.Settings.Compression.EncryptFileNamesDescription' = '同时加密压缩包内的文件名列表(头部)。没有密码连内容也无法浏览。'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'ZIP 格式的规范不支持文件名(中央目录)加密。切换到 7z 即可启用此选项。'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = '密码输入方式'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = '每次拖放时确认'
            'Text.Settings.Compression.PasswordMode.Remember' = '保存并重复使用 (DPAPI 加密)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = '密码:已设置'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = '密码:未设置 (下次压缩时将要求设置)'
            'Text.Settings.Compression.ChangeSavedPassword' = '修改密码'
            'Text.Settings.Compression.ClearSavedPassword' = '删除密码'
            'Text.Password.SetTitle' = '设置密码'
            'Text.Password.SetMessage' = '请设置用于加密压缩包的密码。为了确认,请输入两次。'
            'Text.Password.ConfirmPlaceholder' = '密码 (确认)'
            'Text.Password.MismatchWarning' = '两次输入的密码不一致,请重新输入。'
            'Text.Password.EmptyPasswordWarning' = '请输入密码。'
            'Text.Password.PasteHint' = '可以从密码管理器粘贴 (Ctrl+V)。'
            'Text.Confirm.WipeSavedPassword.Title' = '是否删除已保存的密码?'
            'Text.Confirm.WipeSavedPassword.Message' = '切换到"每次拖放时确认"将会删除当前保存的密码。确定继续吗?'
            'Text.Confirm.ClearSavedPassword.Title' = '是否删除已保存的密码?'
            'Text.Confirm.ClearSavedPassword.Message' = '将删除已保存的密码。下次压缩时需要重新输入。确定继续吗?'
            'Text.Notify.SavedPasswordDecryptFailed' = '无法恢复已保存的密码 (可能是从其他电脑复制设置或 Windows 密码被重置导致)。请重新设置密码。'
            'Text.Notify.PartialSkipWithPassword' = '{0} 个文件因无法访问被跳过。已使用其余文件创建了受密码保护的压缩包。'
            'Text.Error.AllSourcesInaccessible' = '所有源文件都无法访问,因此已中止压缩。未创建空的压缩包。'
            'Text.Error.PasswordNotSupportedByFormat' = '{0} 格式不支持密码保护。请选择 ZIP 或 7z。'
        }
    }

    'zh_TW' = @{
        Comment = '密碼保護 (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = '密碼保護'
            'Text.Settings.Compression.EnablePassword' = '使用密碼保護'
            'Text.Settings.Compression.EnablePasswordDescription' = 'ZIP 以 AES-256 (WinZip AE-2) 加密,7z 以 AES-256 加密。'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR 格式不支援密碼保護。請選擇 ZIP 或 7z。'
            'Text.Settings.Compression.ZipAesExplorerNote' = '注意:AES-256 加密的 ZIP 無法以 Windows 內建的檔案總管解壓縮。收件人需要 7-Zip、WinRAR 等相容工具。'
            'Text.Settings.Compression.EncryptFileNames' = '同時加密檔名'
            'Text.Settings.Compression.EncryptFileNamesDescription' = '同時加密壓縮檔內的檔名清單(標頭)。沒有密碼連內容也無法瀏覽。'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'ZIP 格式的規範不支援檔名(中央目錄)加密。切換為 7z 即可啟用此選項。'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = '密碼輸入方式'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = '每次拖放時確認'
            'Text.Settings.Compression.PasswordMode.Remember' = '儲存並重複使用 (DPAPI 加密)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = '密碼:已設定'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = '密碼:未設定 (下次壓縮時會要求設定)'
            'Text.Settings.Compression.ChangeSavedPassword' = '變更密碼'
            'Text.Settings.Compression.ClearSavedPassword' = '刪除密碼'
            'Text.Password.SetTitle' = '設定密碼'
            'Text.Password.SetMessage' = '請設定用於加密壓縮檔的密碼。為了確認,請輸入兩次。'
            'Text.Password.ConfirmPlaceholder' = '密碼 (確認)'
            'Text.Password.MismatchWarning' = '兩次輸入的密碼不一致,請重新輸入。'
            'Text.Password.EmptyPasswordWarning' = '請輸入密碼。'
            'Text.Password.PasteHint' = '可以從密碼管理工具貼上 (Ctrl+V)。'
            'Text.Confirm.WipeSavedPassword.Title' = '要刪除已儲存的密碼嗎?'
            'Text.Confirm.WipeSavedPassword.Message' = '切換為「每次拖放時確認」將會刪除目前儲存的密碼。確定要繼續嗎?'
            'Text.Confirm.ClearSavedPassword.Title' = '要刪除已儲存的密碼嗎?'
            'Text.Confirm.ClearSavedPassword.Message' = '將刪除已儲存的密碼。下次壓縮時需要重新輸入。確定要繼續嗎?'
            'Text.Notify.SavedPasswordDecryptFailed' = '無法還原已儲存的密碼 (可能是從其他電腦複製設定或 Windows 密碼重設所致)。請重新設定密碼。'
            'Text.Notify.PartialSkipWithPassword' = '{0} 個檔案因無法存取而被跳過。已使用其餘檔案建立了受密碼保護的壓縮檔。'
            'Text.Error.AllSourcesInaccessible' = '所有來源檔案皆無法存取,已中止壓縮。未建立空的壓縮檔。'
            'Text.Error.PasswordNotSupportedByFormat' = '{0} 格式不支援密碼保護。請選擇 ZIP 或 7z。'
        }
    }

    'ko_KR' = @{
        Comment = '암호 보호 (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = '암호 보호'
            'Text.Settings.Compression.EnablePassword' = '암호로 보호'
            'Text.Settings.Compression.EnablePasswordDescription' = 'ZIP은 AES-256 (WinZip AE-2), 7z은 AES-256으로 암호화합니다.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR 형식은 암호 보호를 지원하지 않습니다. ZIP 또는 7z을 선택하세요.'
            'Text.Settings.Compression.ZipAesExplorerNote' = '주의: AES-256으로 암호화된 ZIP은 Windows 기본 탐색기에서 열 수 없습니다. 수신자는 7-Zip, WinRAR 등 호환 도구가 필요합니다.'
            'Text.Settings.Compression.EncryptFileNames' = '파일명도 암호화'
            'Text.Settings.Compression.EncryptFileNamesDescription' = '아카이브 내 파일명 목록(헤더)도 암호화합니다. 암호 없이는 내용을 볼 수도 없게 됩니다.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'ZIP 형식의 사양상 파일명(중앙 디렉터리)은 암호화할 수 없습니다. 7z으로 전환하면 이 옵션을 사용할 수 있습니다.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = '암호 입력 방식'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = '드롭할 때마다 확인'
            'Text.Settings.Compression.PasswordMode.Remember' = '저장하여 재사용 (DPAPI 암호화)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = '암호: 설정됨'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = '암호: 미설정 (다음 압축 시 설정 요청)'
            'Text.Settings.Compression.ChangeSavedPassword' = '암호 변경'
            'Text.Settings.Compression.ClearSavedPassword' = '암호 삭제'
            'Text.Password.SetTitle' = '암호 설정'
            'Text.Password.SetMessage' = '아카이브를 암호화할 암호를 설정하세요. 확인을 위해 두 번 입력합니다.'
            'Text.Password.ConfirmPlaceholder' = '암호 (확인)'
            'Text.Password.MismatchWarning' = '암호가 일치하지 않습니다. 확인란을 다시 입력하세요.'
            'Text.Password.EmptyPasswordWarning' = '암호를 입력하세요.'
            'Text.Password.PasteHint' = '암호 관리자에서 붙여넣기할 수 있습니다 (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = '저장된 암호를 삭제할까요?'
            'Text.Confirm.WipeSavedPassword.Message' = '"드롭할 때마다 확인"으로 전환하면 현재 저장된 암호가 삭제됩니다. 계속할까요?'
            'Text.Confirm.ClearSavedPassword.Title' = '저장된 암호를 삭제할까요?'
            'Text.Confirm.ClearSavedPassword.Message' = '저장된 암호를 삭제합니다. 다음 압축 시 다시 입력해야 합니다. 계속할까요?'
            'Text.Notify.SavedPasswordDecryptFailed' = '저장된 암호를 복원하지 못했습니다 (다른 PC에서 설정 복사 또는 Windows 암호 재설정이 원인일 수 있음). 암호를 다시 설정하세요.'
            'Text.Notify.PartialSkipWithPassword' = '{0}개 파일이 접근 불가로 건너뛰어졌습니다. 나머지 파일로 암호 보호된 아카이브를 만들었습니다.'
            'Text.Error.AllSourcesInaccessible' = '모든 원본 파일에 접근할 수 없어 압축을 중단했습니다. 빈 아카이브는 생성되지 않았습니다.'
            'Text.Error.PasswordNotSupportedByFormat' = '{0} 형식은 암호 보호를 지원하지 않습니다. ZIP 또는 7z을 선택하세요.'
        }
    }

    'de_DE' = @{
        Comment = 'Passwortschutz (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Passwortschutz'
            'Text.Settings.Compression.EnablePassword' = 'Mit Passwort schützen'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Verschlüsselt ZIP mit AES-256 (WinZip AE-2) und 7z mit AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR unterstützt keinen Passwortschutz. Wählen Sie ZIP oder 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Hinweis: AES-256-verschlüsselte ZIPs können nicht mit dem in Windows integrierten Explorer geöffnet werden. Empfänger benötigen 7-Zip, WinRAR oder ein kompatibles Tool.'
            'Text.Settings.Compression.EncryptFileNames' = 'Auch Dateinamen verschlüsseln'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Verschlüsselt die Dateinamenliste (Header) im Archiv. Ohne Passwort kann nicht einmal der Inhalt eingesehen werden.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Das ZIP-Format kann Dateinamen (Zentralverzeichnis) nicht verschlüsseln. Wechseln Sie zu 7z, um diese Option zu aktivieren.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Eingabe des Passworts'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Bei jedem Ablegen abfragen'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Speichern und wiederverwenden (DPAPI-verschlüsselt)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Passwort: gesetzt'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Passwort: nicht gesetzt (wird beim nächsten Komprimieren abgefragt)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Passwort ändern'
            'Text.Settings.Compression.ClearSavedPassword' = 'Passwort löschen'
            'Text.Password.SetTitle' = 'Passwort festlegen'
            'Text.Password.SetMessage' = 'Legen Sie ein Passwort zur Verschlüsselung des Archivs fest. Geben Sie es zur Bestätigung zweimal ein.'
            'Text.Password.ConfirmPlaceholder' = 'Passwort (Bestätigung)'
            'Text.Password.MismatchWarning' = 'Passwörter stimmen nicht überein. Bitte erneut bestätigen.'
            'Text.Password.EmptyPasswordWarning' = 'Bitte geben Sie ein Passwort ein.'
            'Text.Password.PasteHint' = 'Sie können aus einem Passwortmanager einfügen (Strg+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Gespeichertes Passwort löschen?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Wenn Sie auf "Bei jedem Ablegen abfragen" wechseln, wird das aktuell gespeicherte Passwort gelöscht. Fortfahren?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Gespeichertes Passwort löschen?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Das gespeicherte Passwort wird gelöscht. Beim nächsten Komprimieren müssen Sie es erneut eingeben. Fortfahren?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Das gespeicherte Passwort konnte nicht wiederhergestellt werden (möglicherweise durch Übernahme der Einstellungen von einem anderen PC oder Zurücksetzen des Windows-Passworts). Bitte legen Sie das Passwort erneut fest.'
            'Text.Notify.PartialSkipWithPassword' = '{0} Datei(en) wurden übersprungen, da sie nicht zugänglich waren. Mit den restlichen Dateien wurde ein passwortgeschütztes Archiv erstellt.'
            'Text.Error.AllSourcesInaccessible' = 'Alle Quelldateien waren nicht zugänglich, daher wurde die Komprimierung abgebrochen. Es wurde kein leeres Archiv erstellt.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Das Format {0} unterstützt keinen Passwortschutz. Wählen Sie ZIP oder 7z.'
        }
    }

    'fr_FR' = @{
        Comment = 'Protection par mot de passe (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Protection par mot de passe'
            'Text.Settings.Compression.EnablePassword' = 'Protéger par mot de passe'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Chiffre les ZIP avec AES-256 (WinZip AE-2) et les 7z avec AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR ne prend pas en charge la protection par mot de passe. Choisissez ZIP ou 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Remarque : les ZIP chiffrés AES-256 ne peuvent pas être ouverts par l''Explorateur Windows intégré. Les destinataires ont besoin de 7-Zip, WinRAR ou d''un outil compatible.'
            'Text.Settings.Compression.EncryptFileNames' = 'Chiffrer aussi les noms de fichiers'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Chiffre la liste des noms de fichiers (en-tête) dans l''archive. Sans le mot de passe, même le contenu ne peut être parcouru.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Le format ZIP ne peut pas chiffrer les noms de fichiers (répertoire central). Passez à 7z pour activer cette option.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Comment saisir le mot de passe'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Demander à chaque dépôt'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Enregistrer et réutiliser (chiffré DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Mot de passe : défini'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Mot de passe : non défini (demandé à la prochaine compression)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Changer le mot de passe'
            'Text.Settings.Compression.ClearSavedPassword' = 'Supprimer le mot de passe'
            'Text.Password.SetTitle' = 'Définir un mot de passe'
            'Text.Password.SetMessage' = 'Définissez un mot de passe pour chiffrer l''archive. Saisissez-le deux fois pour confirmation.'
            'Text.Password.ConfirmPlaceholder' = 'Mot de passe (confirmation)'
            'Text.Password.MismatchWarning' = 'Les mots de passe ne correspondent pas. Veuillez ressaisir la confirmation.'
            'Text.Password.EmptyPasswordWarning' = 'Veuillez saisir un mot de passe.'
            'Text.Password.PasteHint' = 'Vous pouvez coller depuis un gestionnaire de mots de passe (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Supprimer le mot de passe enregistré ?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Passer à "Demander à chaque dépôt" supprimera le mot de passe actuellement enregistré. Continuer ?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Supprimer le mot de passe enregistré ?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Le mot de passe enregistré sera supprimé. Vous devrez le ressaisir lors de la prochaine compression. Continuer ?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Le mot de passe enregistré n''a pas pu être restauré (probablement à cause d''une copie de paramètres depuis un autre PC ou d''une réinitialisation du mot de passe Windows). Veuillez le redéfinir.'
            'Text.Notify.PartialSkipWithPassword' = '{0} fichier(s) ignoré(s) car inaccessibles. Une archive protégée par mot de passe a été créée avec les fichiers restants.'
            'Text.Error.AllSourcesInaccessible' = 'Tous les fichiers source étaient inaccessibles, la compression a été abandonnée. Aucune archive vide n''a été créée.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Le format {0} ne prend pas en charge la protection par mot de passe. Choisissez ZIP ou 7z.'
        }
    }

    'es_ES' = @{
        Comment = 'Protección con contraseña (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Protección con contraseña'
            'Text.Settings.Compression.EnablePassword' = 'Proteger con contraseña'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Cifra ZIP con AES-256 (WinZip AE-2) y 7z con AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR no admite protección con contraseña. Elija ZIP o 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Nota: los ZIP cifrados con AES-256 no se pueden abrir con el Explorador integrado de Windows. Los destinatarios necesitan 7-Zip, WinRAR u otra herramienta compatible.'
            'Text.Settings.Compression.EncryptFileNames' = 'Cifrar también los nombres de archivo'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Cifra la lista de nombres de archivo (encabezado) dentro del archivo. Sin la contraseña, ni siquiera el contenido se podrá examinar.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'El formato ZIP no puede cifrar los nombres de archivo (directorio central). Cambie a 7z para habilitar esta opción.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Cómo introducir la contraseña'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Preguntar en cada acción de soltar'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Guardar y reutilizar (cifrado DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Contraseña: establecida'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Contraseña: no establecida (se solicitará en la próxima compresión)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Cambiar contraseña'
            'Text.Settings.Compression.ClearSavedPassword' = 'Borrar contraseña'
            'Text.Password.SetTitle' = 'Establecer contraseña'
            'Text.Password.SetMessage' = 'Establezca una contraseña para cifrar el archivo. Introdúzcala dos veces para confirmarla.'
            'Text.Password.ConfirmPlaceholder' = 'Contraseña (confirmación)'
            'Text.Password.MismatchWarning' = 'Las contraseñas no coinciden. Vuelva a introducir la confirmación.'
            'Text.Password.EmptyPasswordWarning' = 'Introduzca una contraseña.'
            'Text.Password.PasteHint' = 'Puede pegar desde un gestor de contraseñas (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = '¿Eliminar la contraseña guardada?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Cambiar a "Preguntar en cada acción de soltar" eliminará la contraseña guardada actualmente. ¿Continuar?'
            'Text.Confirm.ClearSavedPassword.Title' = '¿Eliminar la contraseña guardada?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Se eliminará la contraseña guardada. Deberá introducirla de nuevo en la próxima compresión. ¿Continuar?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'No se pudo restaurar la contraseña guardada (posiblemente debido a una copia de la configuración desde otro PC o un restablecimiento de la contraseña de Windows). Vuelva a establecer la contraseña.'
            'Text.Notify.PartialSkipWithPassword' = 'Se omitieron {0} archivo(s) por ser inaccesibles. Se creó un archivo protegido con contraseña con los archivos restantes.'
            'Text.Error.AllSourcesInaccessible' = 'Todos los archivos de origen eran inaccesibles, por lo que se canceló la compresión. No se creó un archivo vacío.'
            'Text.Error.PasswordNotSupportedByFormat' = 'El formato {0} no admite protección con contraseña. Elija ZIP o 7z.'
        }
    }

    'pt_BR' = @{
        Comment = 'Proteção por senha (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Proteção por senha'
            'Text.Settings.Compression.EnablePassword' = 'Proteger com senha'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Criptografa ZIP com AES-256 (WinZip AE-2) e 7z com AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR não oferece suporte à proteção por senha. Escolha ZIP ou 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Observação: ZIPs criptografados com AES-256 não podem ser abertos pelo Explorador do Windows integrado. Os destinatários precisam de 7-Zip, WinRAR ou outra ferramenta compatível.'
            'Text.Settings.Compression.EncryptFileNames' = 'Criptografar também os nomes de arquivos'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Criptografa a lista de nomes de arquivos (cabeçalho) dentro do arquivo compactado. Sem a senha, nem o conteúdo pode ser navegado.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'O formato ZIP não consegue criptografar os nomes de arquivos (diretório central). Mude para 7z para habilitar esta opção.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Como inserir a senha'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Perguntar a cada arrastar/soltar'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Salvar e reutilizar (criptografado por DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Senha: definida'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Senha: não definida (será solicitada na próxima compactação)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Alterar senha'
            'Text.Settings.Compression.ClearSavedPassword' = 'Excluir senha'
            'Text.Password.SetTitle' = 'Definir uma senha'
            'Text.Password.SetMessage' = 'Defina uma senha para criptografar o arquivo. Insira duas vezes para confirmação.'
            'Text.Password.ConfirmPlaceholder' = 'Senha (confirmação)'
            'Text.Password.MismatchWarning' = 'As senhas não conferem. Reinsira a confirmação.'
            'Text.Password.EmptyPasswordWarning' = 'Insira uma senha.'
            'Text.Password.PasteHint' = 'Você pode colar de um gerenciador de senhas (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Excluir a senha salva?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Mudar para "Perguntar a cada arrastar/soltar" excluirá a senha salva atual. Continuar?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Excluir a senha salva?'
            'Text.Confirm.ClearSavedPassword.Message' = 'A senha salva será excluída. Você precisará digitá-la novamente na próxima compactação. Continuar?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Não foi possível restaurar a senha salva (provavelmente por copiar configurações de outro PC ou redefinir a senha do Windows). Defina a senha novamente.'
            'Text.Notify.PartialSkipWithPassword' = '{0} arquivo(s) ignorado(s) por estarem inacessíveis. Um arquivo protegido por senha foi criado com os arquivos restantes.'
            'Text.Error.AllSourcesInaccessible' = 'Todos os arquivos de origem estavam inacessíveis, então a compactação foi abortada. Nenhum arquivo vazio foi criado.'
            'Text.Error.PasswordNotSupportedByFormat' = 'O formato {0} não oferece suporte à proteção por senha. Escolha ZIP ou 7z.'
        }
    }

    'ru_RU' = @{
        Comment = 'Защита паролем (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Защита паролем'
            'Text.Settings.Compression.EnablePassword' = 'Защитить паролем'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Шифрует ZIP с помощью AES-256 (WinZip AE-2), 7z — с помощью AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR не поддерживает защиту паролем. Выберите ZIP или 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Примечание: ZIP с шифрованием AES-256 невозможно открыть встроенным Проводником Windows. Получателям нужны 7-Zip, WinRAR или другой совместимый инструмент.'
            'Text.Settings.Compression.EncryptFileNames' = 'Шифровать и имена файлов'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Шифрует список имён файлов (заголовок) внутри архива. Без пароля даже содержимое нельзя просмотреть.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Формат ZIP не может шифровать имена файлов (центральный каталог). Переключитесь на 7z, чтобы включить эту опцию.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Способ ввода пароля'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Запрашивать при каждом перетаскивании'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Сохранить и использовать повторно (шифрование DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Пароль: задан'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Пароль: не задан (будет запрошен при следующем сжатии)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Изменить пароль'
            'Text.Settings.Compression.ClearSavedPassword' = 'Удалить пароль'
            'Text.Password.SetTitle' = 'Установить пароль'
            'Text.Password.SetMessage' = 'Установите пароль для шифрования архива. Введите его дважды для подтверждения.'
            'Text.Password.ConfirmPlaceholder' = 'Пароль (подтверждение)'
            'Text.Password.MismatchWarning' = 'Пароли не совпадают. Введите подтверждение ещё раз.'
            'Text.Password.EmptyPasswordWarning' = 'Введите пароль.'
            'Text.Password.PasteHint' = 'Можно вставить из менеджера паролей (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Удалить сохранённый пароль?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Переключение на "Запрашивать при каждом перетаскивании" удалит текущий сохранённый пароль. Продолжить?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Удалить сохранённый пароль?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Сохранённый пароль будет удалён. При следующем сжатии его нужно будет ввести снова. Продолжить?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Не удалось восстановить сохранённый пароль (вероятно, из-за копирования настроек с другого ПК или сброса пароля Windows). Установите пароль заново.'
            'Text.Notify.PartialSkipWithPassword' = 'Пропущено файлов: {0} из-за недоступности. Архив с защитой паролем создан из оставшихся файлов.'
            'Text.Error.AllSourcesInaccessible' = 'Все исходные файлы недоступны, поэтому сжатие отменено. Пустой архив не создан.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Формат {0} не поддерживает защиту паролем. Выберите ZIP или 7z.'
        }
    }

    'it_IT' = @{
        Comment = 'Protezione con password (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Protezione con password'
            'Text.Settings.Compression.EnablePassword' = 'Proteggi con password'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Cifra ZIP con AES-256 (WinZip AE-2) e 7z con AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR non supporta la protezione con password. Scegli ZIP o 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Nota: i file ZIP cifrati con AES-256 non possono essere aperti con l''Esplora risorse integrato di Windows. I destinatari hanno bisogno di 7-Zip, WinRAR o un altro strumento compatibile.'
            'Text.Settings.Compression.EncryptFileNames' = 'Cifra anche i nomi dei file'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Cifra l''elenco dei nomi dei file (intestazione) all''interno dell''archivio. Senza la password non si potrà nemmeno sfogliare il contenuto.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Il formato ZIP non può cifrare i nomi dei file (directory centrale). Passa a 7z per abilitare questa opzione.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Come inserire la password'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Chiedi a ogni rilascio'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Salva e riutilizza (cifrato con DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Password: impostata'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Password: non impostata (verrà chiesta alla prossima compressione)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Cambia password'
            'Text.Settings.Compression.ClearSavedPassword' = 'Elimina password'
            'Text.Password.SetTitle' = 'Imposta una password'
            'Text.Password.SetMessage' = 'Imposta una password per cifrare l''archivio. Inseriscila due volte per conferma.'
            'Text.Password.ConfirmPlaceholder' = 'Password (conferma)'
            'Text.Password.MismatchWarning' = 'Le password non corrispondono. Reinserisci la conferma.'
            'Text.Password.EmptyPasswordWarning' = 'Inserisci una password.'
            'Text.Password.PasteHint' = 'Puoi incollare da un gestore di password (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Eliminare la password salvata?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Passare a "Chiedi a ogni rilascio" eliminerà la password attualmente salvata. Continuare?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Eliminare la password salvata?'
            'Text.Confirm.ClearSavedPassword.Message' = 'La password salvata verrà eliminata. Dovrai reinserirla alla prossima compressione. Continuare?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Impossibile ripristinare la password salvata (probabilmente a causa della copia delle impostazioni da un altro PC o di un reset della password di Windows). Reimposta la password.'
            'Text.Notify.PartialSkipWithPassword' = '{0} file ignorati perché inaccessibili. È stato creato un archivio protetto da password con i file rimanenti.'
            'Text.Error.AllSourcesInaccessible' = 'Tutti i file di origine non erano accessibili, quindi la compressione è stata annullata. Non è stato creato un archivio vuoto.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Il formato {0} non supporta la protezione con password. Scegli ZIP o 7z.'
        }
    }

    'uk_UA' = @{
        Comment = 'Захист паролем (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Захист паролем'
            'Text.Settings.Compression.EnablePassword' = 'Захистити паролем'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Шифрує ZIP за допомогою AES-256 (WinZip AE-2), 7z — за допомогою AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR не підтримує захист паролем. Виберіть ZIP або 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Примітка: ZIP-архіви, зашифровані AES-256, не можна відкрити стандартним Провідником Windows. Одержувачам потрібні 7-Zip, WinRAR або інший сумісний інструмент.'
            'Text.Settings.Compression.EncryptFileNames' = 'Шифрувати також імена файлів'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Шифрує список імен файлів (заголовок) усередині архіву. Без пароля навіть вмісту не побачите.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Формат ZIP не може шифрувати імена файлів (центральний каталог). Перейдіть на 7z, щоб увімкнути цю опцію.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Як вводити пароль'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Запитувати при кожному перетягуванні'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Зберегти й повторно використовувати (зашифровано DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Пароль: встановлено'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Пароль: не встановлено (буде запитано під час наступного стиснення)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Змінити пароль'
            'Text.Settings.Compression.ClearSavedPassword' = 'Видалити пароль'
            'Text.Password.SetTitle' = 'Встановити пароль'
            'Text.Password.SetMessage' = 'Встановіть пароль для шифрування архіву. Введіть його двічі для підтвердження.'
            'Text.Password.ConfirmPlaceholder' = 'Пароль (підтвердження)'
            'Text.Password.MismatchWarning' = 'Паролі не збігаються. Повторно введіть підтвердження.'
            'Text.Password.EmptyPasswordWarning' = 'Введіть пароль.'
            'Text.Password.PasteHint' = 'Можна вставити з менеджера паролів (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Видалити збережений пароль?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Перехід на "Запитувати при кожному перетягуванні" видалить поточний збережений пароль. Продовжити?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Видалити збережений пароль?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Збережений пароль буде видалено. Під час наступного стиснення його доведеться ввести знову. Продовжити?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Не вдалося відновити збережений пароль (імовірно, через копіювання налаштувань з іншого ПК або скидання пароля Windows). Установіть пароль ще раз.'
            'Text.Notify.PartialSkipWithPassword' = 'Пропущено файлів: {0}, оскільки вони були недоступні. З решти файлів створено архів, захищений паролем.'
            'Text.Error.AllSourcesInaccessible' = 'Усі вихідні файли були недоступні, тож стиснення скасовано. Порожній архів не створено.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Формат {0} не підтримує захист паролем. Виберіть ZIP або 7z.'
        }
    }

    'id_ID' = @{
        Comment = 'Perlindungan kata sandi (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Perlindungan Kata Sandi'
            'Text.Settings.Compression.EnablePassword' = 'Lindungi dengan kata sandi'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Mengenkripsi ZIP dengan AES-256 (WinZip AE-2), 7z dengan AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR tidak mendukung perlindungan kata sandi. Pilih ZIP atau 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Catatan: ZIP terenkripsi AES-256 tidak dapat dibuka dengan Windows Explorer bawaan. Penerima memerlukan 7-Zip, WinRAR, atau alat lain yang kompatibel.'
            'Text.Settings.Compression.EncryptFileNames' = 'Enkripsi juga nama file'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Mengenkripsi daftar nama file (header) di dalam arsip. Tanpa kata sandi, bahkan isinya pun tidak dapat dijelajahi.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Spesifikasi format ZIP tidak dapat mengenkripsi nama file (direktori pusat). Beralih ke 7z untuk mengaktifkan opsi ini.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Cara memasukkan kata sandi'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Tanyakan setiap kali drop'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Simpan dan gunakan kembali (terenkripsi DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Kata sandi: telah diatur'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Kata sandi: belum diatur (akan diminta pada kompresi berikutnya)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Ubah Kata Sandi'
            'Text.Settings.Compression.ClearSavedPassword' = 'Hapus Kata Sandi'
            'Text.Password.SetTitle' = 'Atur Kata Sandi'
            'Text.Password.SetMessage' = 'Atur kata sandi untuk mengenkripsi arsip. Masukkan dua kali untuk konfirmasi.'
            'Text.Password.ConfirmPlaceholder' = 'Kata sandi (konfirmasi)'
            'Text.Password.MismatchWarning' = 'Kata sandi tidak cocok. Silakan masukkan ulang konfirmasi.'
            'Text.Password.EmptyPasswordWarning' = 'Silakan masukkan kata sandi.'
            'Text.Password.PasteHint' = 'Anda dapat menempel dari pengelola kata sandi (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Hapus kata sandi yang tersimpan?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Beralih ke "Tanyakan setiap kali drop" akan menghapus kata sandi yang saat ini tersimpan. Lanjutkan?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Hapus kata sandi yang tersimpan?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Kata sandi yang tersimpan akan dihapus. Anda perlu memasukkannya kembali pada kompresi berikutnya. Lanjutkan?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Kata sandi yang tersimpan tidak dapat dipulihkan (kemungkinan disebabkan oleh menyalin pengaturan dari PC lain atau reset kata sandi Windows). Silakan atur kata sandi lagi.'
            'Text.Notify.PartialSkipWithPassword' = '{0} berkas dilewati karena tidak dapat diakses. Arsip yang dilindungi kata sandi telah dibuat dari berkas yang tersisa.'
            'Text.Error.AllSourcesInaccessible' = 'Semua berkas sumber tidak dapat diakses, sehingga kompresi dibatalkan. Arsip kosong tidak dibuat.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Format {0} tidak mendukung perlindungan kata sandi. Pilih ZIP atau 7z.'
        }
    }

    'ta_IN' = @{
        Comment = 'கடவுச்சொல் பாதுகாப்பு (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'கடவுச்சொல் பாதுகாப்பு'
            'Text.Settings.Compression.EnablePassword' = 'கடவுச்சொல்லால் பாதுகாக்கவும்'
            'Text.Settings.Compression.EnablePasswordDescription' = 'ZIP-க்கு AES-256 (WinZip AE-2), 7z-க்கு AES-256 மூலம் குறியாக்குகிறது.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR கடவுச்சொல் பாதுகாப்பை ஆதரிக்காது. ZIP அல்லது 7z-ஐ தேர்வுசெய்க.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'குறிப்பு: AES-256 மூலம் குறியாக்கப்பட்ட ZIP-ஐ Windows-இன் உள்ளமைக்கப்பட்ட Explorer மூலம் திறக்க முடியாது. பெறுபவர்களுக்கு 7-Zip, WinRAR அல்லது இணக்கமான கருவி தேவை.'
            'Text.Settings.Compression.EncryptFileNames' = 'கோப்பு பெயர்களையும் குறியாக்கு'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'காப்பகத்தினுள் உள்ள கோப்பு பெயர்கள் பட்டியலையும் (தலைப்பு) குறியாக்குகிறது. கடவுச்சொல் இல்லாமல் உள்ளடக்கத்தைக் கூட பார்க்க முடியாது.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'ZIP வடிவத்தின் விவரக்குறிப்புப்படி கோப்பு பெயர்களை (மைய அடைவு) குறியாக்க முடியாது. இந்த விருப்பத்தை இயக்க 7z-க்கு மாறவும்.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'கடவுச்சொல் உள்ளீட்டு முறை'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'ஒவ்வொரு போடுதலிலும் கேள்'
            'Text.Settings.Compression.PasswordMode.Remember' = 'சேமித்து மீண்டும் பயன்படுத்து (DPAPI குறியாக்கம்)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'கடவுச்சொல்: அமைக்கப்பட்டது'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'கடவுச்சொல்: அமைக்கப்படவில்லை (அடுத்த அமுக்கத்தில் கேட்கப்படும்)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'கடவுச்சொல்லை மாற்று'
            'Text.Settings.Compression.ClearSavedPassword' = 'கடவுச்சொல்லை அழி'
            'Text.Password.SetTitle' = 'கடவுச்சொல்லை அமை'
            'Text.Password.SetMessage' = 'காப்பகத்தை குறியாக்க கடவுச்சொல்லை அமைக்கவும். உறுதிப்படுத்த இருமுறை உள்ளிடவும்.'
            'Text.Password.ConfirmPlaceholder' = 'கடவுச்சொல் (உறுதிப்படுத்தல்)'
            'Text.Password.MismatchWarning' = 'கடவுச்சொற்கள் பொருந்தவில்லை. உறுதிப்படுத்தலை மீண்டும் உள்ளிடவும்.'
            'Text.Password.EmptyPasswordWarning' = 'கடவுச்சொல்லை உள்ளிடவும்.'
            'Text.Password.PasteHint' = 'கடவுச்சொல் மேலாளரிலிருந்து ஒட்டலாம் (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'சேமிக்கப்பட்ட கடவுச்சொல்லை அழிக்கவா?'
            'Text.Confirm.WipeSavedPassword.Message' = '"ஒவ்வொரு போடுதலிலும் கேள்" என்பதற்கு மாறுவது தற்போது சேமிக்கப்பட்டுள்ள கடவுச்சொல்லை அழிக்கும். தொடரவா?'
            'Text.Confirm.ClearSavedPassword.Title' = 'சேமிக்கப்பட்ட கடவுச்சொல்லை அழிக்கவா?'
            'Text.Confirm.ClearSavedPassword.Message' = 'சேமிக்கப்பட்ட கடவுச்சொல் அழிக்கப்படும். அடுத்த அமுக்கத்தில் மீண்டும் உள்ளிட வேண்டியிருக்கும். தொடரவா?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'சேமிக்கப்பட்ட கடவுச்சொல்லை மீட்டெடுக்க முடியவில்லை (வேறு கணினியிலிருந்து அமைப்புகளை நகலெடுத்தது அல்லது Windows கடவுச்சொல் மீட்டமைப்பு காரணமாக இருக்கலாம்). கடவுச்சொல்லை மீண்டும் அமைக்கவும்.'
            'Text.Notify.PartialSkipWithPassword' = 'அணுக முடியாததால் {0} கோப்புகள் தவிர்க்கப்பட்டன. மீதமுள்ள கோப்புகளுடன் கடவுச்சொல்லால் பாதுகாக்கப்பட்ட காப்பகம் உருவாக்கப்பட்டது.'
            'Text.Error.AllSourcesInaccessible' = 'அனைத்து மூல கோப்புகளும் அணுக முடியாதவை, எனவே அமுக்கம் இரத்து செய்யப்பட்டது. வெற்று காப்பகம் உருவாக்கப்படவில்லை.'
            'Text.Error.PasswordNotSupportedByFormat' = '{0} வடிவம் கடவுச்சொல் பாதுகாப்பை ஆதரிக்காது. ZIP அல்லது 7z-ஐ தேர்வுசெய்க.'
        }
    }

    'sa_IN' = @{
        Comment = 'गुप्तशब्द-रक्षा (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'गुप्तशब्द-रक्षा'
            'Text.Settings.Compression.EnablePassword' = 'गुप्तशब्देन रक्षतु'
            'Text.Settings.Compression.EnablePasswordDescription' = 'ZIP-कृते AES-256 (WinZip AE-2), 7z-कृते AES-256 इत्यनेन गोपयति।'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR-रूपं गुप्तशब्द-रक्षां न समर्थयति। ZIP वा 7z वा चिनोतु।'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'सूचना: AES-256-गुप्तीकृताः ZIP-सञ्चिकाः Windows-स्य अन्तर्गतेन Explorer-इत्यनेन उद्घाटयितुं न शक्यन्ते। ग्राहकेभ्यः 7-Zip, WinRAR वा अन्यः अनुकूलः उपकरणः आवश्यकः।'
            'Text.Settings.Compression.EncryptFileNames' = 'सञ्चिकानामान्यपि गोपयतु'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'सञ्चयस्य अन्तर्भागे सञ्चिकानाम-सूचीम् (शीर्षकम्) अपि गोपयति। गुप्तशब्दं विना अन्तर्वस्तुनोऽपि अवलोकनं न शक्यम्।'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'ZIP-रूपस्य निर्देशानुसारं सञ्चिकानामानि (केन्द्रनिर्देशिका) गोपयितुं न शक्यन्ते। एतद्विकल्पं समर्थयितुं 7z-रूपं चिनोतु।'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'गुप्तशब्दं प्रविशन्तु इति प्रकारः'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'प्रतिप्रयोगे पृच्छतु'
            'Text.Settings.Compression.PasswordMode.Remember' = 'सञ्चयतु पुनः उपयुङ्क्ताम् (DPAPI-गुप्तीकृतः)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'गुप्तशब्दः: स्थापितः'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'गुप्तशब्दः: न स्थापितः (आगामि-सङ्कोचने पृष्टं भविष्यति)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'गुप्तशब्दं परिवर्तयतु'
            'Text.Settings.Compression.ClearSavedPassword' = 'गुप्तशब्दं लोपयतु'
            'Text.Password.SetTitle' = 'गुप्तशब्दं स्थापयतु'
            'Text.Password.SetMessage' = 'सञ्चयस्य गोपनार्थं गुप्तशब्दं स्थापयतु। पुष्ट्यर्थं द्विवारं प्रविशन्तु।'
            'Text.Password.ConfirmPlaceholder' = 'गुप्तशब्दः (पुष्टिः)'
            'Text.Password.MismatchWarning' = 'गुप्तशब्दौ न समानौ। पुनः पुष्टिं प्रविशन्तु।'
            'Text.Password.EmptyPasswordWarning' = 'गुप्तशब्दं प्रविशन्तु।'
            'Text.Password.PasteHint' = 'गुप्तशब्द-व्यवस्थापकात् लेपयितुं शक्नुथ (Ctrl+V)।'
            'Text.Confirm.WipeSavedPassword.Title' = 'सञ्चितं गुप्तशब्दं लोपयन्तु वा?'
            'Text.Confirm.WipeSavedPassword.Message' = '"प्रतिप्रयोगे पृच्छतु" इत्यत्र परिवर्तनेन साम्प्रतं सञ्चितं गुप्तशब्दं लोपयिष्यते। अनुवर्तताम् वा?'
            'Text.Confirm.ClearSavedPassword.Title' = 'सञ्चितं गुप्तशब्दं लोपयन्तु वा?'
            'Text.Confirm.ClearSavedPassword.Message' = 'सञ्चितः गुप्तशब्दः लोप्स्यते। आगामि-सङ्कोचने पुनः प्रवेशनम् आवश्यकं भविष्यति। अनुवर्तताम् वा?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'सञ्चितं गुप्तशब्दं प्रत्यानेतुं न शक्तम् (अन्यस्मात् कणित्रात् व्यवस्थानां प्रतिलेखनं Windows-गुप्तशब्द-पुनः स्थापनं वा कारणं स्यात्)। गुप्तशब्दं पुनः स्थापयतु।'
            'Text.Notify.PartialSkipWithPassword' = '{0} सञ्चिकाः अप्राप्यत्वात् त्यक्ताः। शेषाभिः सञ्चिकाभिः गुप्तशब्द-रक्षितः सञ्चयः कृतः।'
            'Text.Error.AllSourcesInaccessible' = 'सर्वाः मूलसञ्चिकाः अप्राप्याः आसन्, अतः सङ्कोचनं विरतम्। रिक्तः सञ्चयः न रचितः।'
            'Text.Error.PasswordNotSupportedByFormat' = '{0} रूपं गुप्तशब्द-रक्षां न समर्थयति। ZIP वा 7z वा चिनोतु।'
        }
    }

    'la_VA' = @{
        Comment = 'Tutela tessera (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Tutela tessera'
            'Text.Settings.Compression.EnablePassword' = 'Tessera muniri'
            'Text.Settings.Compression.EnablePasswordDescription' = 'ZIP per AES-256 (WinZip AE-2), 7z per AES-256 cryptat.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'TAR tutelam tesserae non sustinet. ZIP vel 7z eligere.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Nota: archiva ZIP per AES-256 cryptata Exploratore Windows inserto aperiri non possunt. Receptoribus 7-Zip, WinRAR vel aliud instrumentum compatibile opus est.'
            'Text.Settings.Compression.EncryptFileNames' = 'Etiam nomina fasciculorum cryptare'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Indicem nominum fasciculorum (caput) intra archivum cryptat. Sine tessera ne contenta quidem inspici possunt.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Forma ZIP nomina fasciculorum (indicem centralem) cryptare non potest. Ad 7z muta ut haec optio activari possit.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Quomodo tesseram inserere'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Singulis demissionibus rogare'
            'Text.Settings.Compression.PasswordMode.Remember' = 'Servare et iterum uti (DPAPI cryptatum)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Tessera: constituta'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Tessera: non constituta (proxima compressione rogabitur)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Tesseram mutare'
            'Text.Settings.Compression.ClearSavedPassword' = 'Tesseram delere'
            'Text.Password.SetTitle' = 'Tesseram constituere'
            'Text.Password.SetMessage' = 'Tesseram ad archivum cryptandum constitue. Ad confirmandum bis insere.'
            'Text.Password.ConfirmPlaceholder' = 'Tessera (confirmatio)'
            'Text.Password.MismatchWarning' = 'Tesserae non congruunt. Confirmationem iterum insere.'
            'Text.Password.EmptyPasswordWarning' = 'Tesseram insere.'
            'Text.Password.PasteHint' = 'Ex tesserarum administro inserere potes (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Tesseram servatam delere?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Ad "Singulis demissionibus rogare" mutatio tesseram nunc servatam delebit. Pergere?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Tesseram servatam delere?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Tessera servata delebitur. Proxima compressione iterum inserere debes. Pergere?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Tessera servata restitui non potuit (fortasse ob configurationes ex alio computatro descriptas vel ob tesseram Windows redintegratam). Tesseram iterum constitue.'
            'Text.Notify.PartialSkipWithPassword' = '{0} fasciculus(i) praeteritus(i) sunt ob inaccessibilitatem. Archivum tessera tutum cum reliquis fasciculis creatum est.'
            'Text.Error.AllSourcesInaccessible' = 'Omnes fasciculi originales inaccessibiles erant, itaque compressio abrupta est. Archivum inane non creatum est.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Forma {0} tutelam tesserae non sustinet. ZIP vel 7z elige.'
        }
    }

    'fil_PH' = @{
        Comment = 'Proteksyon ng password (v1.0.181+)'
        Strings = [ordered]@{
            'Text.Settings.Compression.PasswordHeader' = 'Proteksyon ng Password'
            'Text.Settings.Compression.EnablePassword' = 'Protektahan gamit ang password'
            'Text.Settings.Compression.EnablePasswordDescription' = 'Ine-encrypt ang ZIP gamit ang AES-256 (WinZip AE-2), 7z gamit ang AES-256.'
            'Text.Settings.Compression.TarNoEncryptionNote' = 'Hindi sinusuportahan ng TAR ang proteksyon ng password. Pumili ng ZIP o 7z.'
            'Text.Settings.Compression.ZipAesExplorerNote' = 'Tandaan: Hindi mabubuksan ng built-in na Windows Explorer ang mga ZIP na naka-encrypt sa AES-256. Kailangan ng mga tatanggap ng 7-Zip, WinRAR, o ibang katugmang tool.'
            'Text.Settings.Compression.EncryptFileNames' = 'I-encrypt din ang mga pangalan ng file'
            'Text.Settings.Compression.EncryptFileNamesDescription' = 'Ine-encrypt ang listahan ng mga pangalan ng file (header) sa loob ng archive. Kahit ang nilalaman ay hindi mata-browse nang walang password.'
            'Text.Settings.Compression.EncryptFileNamesZipUnsupported' = 'Hindi maaaring i-encrypt ng ZIP format ang mga pangalan ng file (central directory). Lumipat sa 7z para paganahin ang opsyong ito.'
            'Text.Settings.Compression.PasswordMode.GroupLabel' = 'Paano ipasok ang password'
            'Text.Settings.Compression.PasswordMode.PromptEachTime' = 'Magtanong tuwing maghuhulog'
            'Text.Settings.Compression.PasswordMode.Remember' = 'I-save at gamitin muli (naka-encrypt sa DPAPI)'
            'Text.Settings.Compression.SavedPasswordStatus.Set' = 'Password: nakatakda'
            'Text.Settings.Compression.SavedPasswordStatus.NotSet' = 'Password: hindi nakatakda (hihilingin sa susunod na compression)'
            'Text.Settings.Compression.ChangeSavedPassword' = 'Baguhin ang Password'
            'Text.Settings.Compression.ClearSavedPassword' = 'Burahin ang Password'
            'Text.Password.SetTitle' = 'Magtakda ng Password'
            'Text.Password.SetMessage' = 'Magtakda ng password para i-encrypt ang archive. Ilagay nang dalawang beses para sa kumpirmasyon.'
            'Text.Password.ConfirmPlaceholder' = 'Password (kumpirmasyon)'
            'Text.Password.MismatchWarning' = 'Hindi tumutugma ang mga password. Pakiilagay muli ang kumpirmasyon.'
            'Text.Password.EmptyPasswordWarning' = 'Mangyaring magpasok ng password.'
            'Text.Password.PasteHint' = 'Maaari kang mag-paste mula sa password manager (Ctrl+V).'
            'Text.Confirm.WipeSavedPassword.Title' = 'Burahin ang naka-save na password?'
            'Text.Confirm.WipeSavedPassword.Message' = 'Ang paglipat sa "Magtanong tuwing maghuhulog" ay magbubura ng kasalukuyang naka-save na password. Magpatuloy?'
            'Text.Confirm.ClearSavedPassword.Title' = 'Burahin ang naka-save na password?'
            'Text.Confirm.ClearSavedPassword.Message' = 'Buburahin ang naka-save na password. Kakailanganin mong ipasok muli sa susunod na compression. Magpatuloy?'
            'Text.Notify.SavedPasswordDecryptFailed' = 'Hindi naibalik ang naka-save na password (malamang dahil sa pagkopya ng mga setting mula sa ibang PC o pag-reset ng Windows password). Pakitakda muli ang password.'
            'Text.Notify.PartialSkipWithPassword' = 'Nilaktawan ang {0} (na) file dahil hindi na-access. Ang archive na protektado ng password ay nilikha gamit ang natitirang mga file.'
            'Text.Error.AllSourcesInaccessible' = 'Hindi na-access ang lahat ng source file, kaya kinansela ang compression. Walang nilikhang walang lamang archive.'
            'Text.Error.PasswordNotSupportedByFormat' = 'Ang {0} format ay hindi sumusuporta sa proteksyon ng password. Pumili ng ZIP o 7z.'
        }
    }
}

# 各ロケールに対して: コメント行を置換 + 各 x:String 値を置換 (キー名で grep)
foreach ($locale in $translations.Keys) {
    $path = Join-Path $localesDir "$locale.axaml"
    if (-not (Test-Path $path)) {
        Write-Warning "skip (missing): $locale"
        continue
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $content = [System.Text.Encoding]::UTF8.GetString($bytes)

    $info = $translations[$locale]
    $newComment = "  <!-- $($info.Comment) -->"

    # コメント置換 (行頭 2 スペース + `<!-- Password protection (v1.0.181+) ...` から行末まで)
    $commentPattern = '(?m)^[ \t]*<!-- Password protection \(v1\.0\.181\+\)[^\r\n]*-->'
    $content = [System.Text.RegularExpressions.Regex]::Replace($content, $commentPattern, $newComment)

    # 各 x:String の値を置換 (キー名でアンカー)
    foreach ($key in $info.Strings.Keys) {
        $value = $info.Strings[$key]
        # XML エスケープ (& は &amp;)
        $escapedValue = $value -replace '&', '&amp;'

        # キーで該当行を見つけて値部分のみ差し替え
        $keyRegex = [regex]::Escape($key)
        # 行全体パターン: `  <x:String x:Key="KEY" xml:space="preserve">VALUE</x:String>`
        $linePattern = '<x:String x:Key="' + $keyRegex + '" xml:space="preserve">[^<]*</x:String>'
        $replacement = '<x:String x:Key="' + $key + '" xml:space="preserve">' + $escapedValue + '</x:String>'
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, $linePattern, $replacement)
    }

    # UTF-8 (BOM なし) で書き戻し
    $outBytes = [System.Text.Encoding]::UTF8.GetBytes($content)
    [System.IO.File]::WriteAllBytes($path, $outBytes)
    Write-Host "translated: $locale"
}

Write-Host ""
Write-Host "Done. Run: dotnet test to verify LocaleParityTests."
