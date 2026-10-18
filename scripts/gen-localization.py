#!/usr/bin/env python3
"""Generate the .resx satellite resources for Pegged.

The master table below is the single source of truth: one key, one entry per
language. Running the script rewrites:

    src/Pegged/Resources/Strings.resx          (neutral = English)
    src/Pegged/Resources/Strings.<culture>.resx

    python scripts/gen-localization.py
"""
import os
import xml.sax.saxutils as su

LANGUAGES = ["zh-Hans", "es", "fr", "ja", "ko", "pt-BR", "tr", "az"]

# key -> (english, zh-Hans, es, fr, ja, ko, pt-BR, tr, az)
T = {
    # tray menu
    "Tray.ShowLine": ("Show line", "显示挂绳", "Mostrar la cuerda", "Afficher la corde",
                      "ラインを表示", "줄 보이기", "Mostrar a corda", "Çizgiyi göster", "Xətti göstər"),
    "Tray.HideLine": ("Hide line", "隐藏挂绳", "Ocultar la cuerda", "Masquer la corde",
                      "ラインを隠す", "줄 숨기기", "Ocultar a corda", "Çizgiyi gizle", "Xətti gizlət"),
    "Tray.ClearAll": ("Take everything down", "全部取下", "Bajar todo", "Tout décrocher",
                      "すべて取り込む", "모두 내리기", "Tirar tudo", "Hepsini indir", "Hamısını yığışdır"),
    "Tray.Settings": ("Settings…", "设置…", "Ajustes…", "Paramètres…",
                      "設定…", "설정…", "Configurações…", "Ayarlar…", "Parametrlər…"),
    "Tray.OpenScreenshotsFolder": ("Open screenshots folder", "打开截图文件夹", "Abrir la carpeta de capturas",
                                   "Ouvrir le dossier des captures", "スクリーンショットフォルダを開く",
                                   "스크린샷 폴더 열기", "Abrir a pasta de capturas",
                                   "Ekran görüntüsü klasörünü aç", "Ekran görüntüsü qovluğunu aç"),
    "Tray.Sounds": ("Sounds", "声音", "Sonidos", "Sons", "サウンド", "소리", "Sons", "Sesler", "Səslər"),
    "Tray.Empty": ("Pegged — the line is empty", "Pegged — 挂绳是空的", "Pegged — la cuerda está vacía",
                   "Pegged — la corde est vide", "Pegged — ラインは空です", "Pegged — 줄이 비어 있습니다",
                   "Pegged — a corda está vazia", "Pegged — çizgi boş", "Pegged — xətt boşdur"),
    "Tray.OnLine": ("Pegged — {0} on the line", "Pegged — 挂了 {0} 张", "Pegged — {0} en la cuerda",
                    "Pegged — {0} sur la corde", "Pegged — {0} 件", "Pegged — {0}개",
                    "Pegged — {0} na corda", "Pegged — çizgide {0}", "Pegged — xətdə {0}"),
    "Tray.OpenAtLogin": ("Open at login", "开机时启动", "Abrir al iniciar sesión", "Ouvrir à la connexion",
                         "ログイン時に開く", "로그인 시 열기", "Abrir ao entrar",
                         "Oturum açarken başlat", "Girişdə aç"),
    "Tray.CheckForUpdates": ("Check for Updates…", "检查更新…", "Buscar actualizaciones…",
                             "Rechercher des mises à jour…", "アップデートを確認…",
                             "업데이트 확인…", "Procurar atualizações…", "Güncellemeleri denetle…",
                             "Yenilənmələri yoxla…"),
    "Tray.Quit": ("Quit Pegged", "退出 Pegged", "Salir de Pegged", "Quitter Pegged",
                  "Pegged を終了", "Pegged 종료", "Sair do Pegged", "Pegged'den çık", "Pegged-dən çıx"),

    # bring-the-line-down conditions
    "Menu.BringLineDown": ("Bring the line down", "让挂绳落下", "Bajar la cuerda", "Faire descendre la corde",
                           "ラインを下ろす", "줄 내리기", "Descer a corda", "Çizgiyi indir", "Xətti endir"),
    "Menu.ConditionTopEdge": ("When the pointer rests at the top edge", "指针停在屏幕顶边时",
                              "Cuando el puntero se detiene en el borde superior",
                              "Quand le pointeur s'arrête en haut de l'écran",
                              "ポインターが上端に止まったとき", "포인터가 위쪽 가장자리에 머물 때",
                              "Quando o ponteiro para na borda superior",
                              "İşaretçi üst kenarda durduğunda", "Göstərici yuxarı kənarda dayandıqda"),
    "Menu.ConditionNew": ("When something new hangs", "有新内容挂上时", "Cuando cuelga algo nuevo",
                          "Quand quelque chose de nouveau est accroché", "新しいものが掛かったとき",
                          "새 항목이 걸릴 때", "Quando algo novo é pendurado",
                          "Yeni bir şey asıldığında", "Yeni bir şey asılanda"),
    "Menu.ConditionFullScreen": ("Over full screen apps", "在全屏应用之上", "Sobre apps en pantalla completa",
                                 "Au-dessus des apps plein écran", "全画面アプリの上に",
                                 "전체 화면 앱 위에", "Sobre apps em tela cheia",
                                 "Tam ekran uygulamaların üzerinde", "Tam ekran tətbiqlərin üzərində"),
    "Menu.WatchClipboard": ("Hang copied images", "挂上复制的图片", "Colgar imágenes copiadas",
                            "Accrocher les images copiées", "コピーした画像を掛ける",
                            "복사한 이미지 걸기", "Pendurar imagens copiadas",
                            "Kopyalanan görüntüleri as", "Kopyalanmış şəkilləri as"),
    "Menu.WatchClipboardHint": ("Images you copy hang on the line too", "复制的图片也会挂到绳上",
                                "Las imágenes que copies también cuelgan de la cuerda",
                                "Les images que vous copiez pendent aussi à la corde",
                                "コピーした画像もラインに掛かります",
                                "복사한 이미지도 줄에 걸립니다",
                                "As imagens que você copia também ficam na corda",
                                "Kopyaladığınız görüntüler de çizgiye asılır",
                                "Kopyaladığınız şəkillər də xəttə asılır"),

    # folders
    "Menu.ExtraFolders": ("Also watch folders", "同时监控文件夹", "Carpetas que también vigilar",
                          "Dossiers également surveillés", "監視するフォルダ",
                          "함께 볼 폴더", "Pastas que também observar",
                          "Ayrıca izlenecek klasörler", "Əlavə izlənilən qovluqlar"),
    "Menu.AddFolder": ("Add Folder…", "添加文件夹…", "Añadir carpeta…", "Ajouter un dossier…",
                       "フォルダを追加…", "폴더 추가…", "Adicionar pasta…", "Klasör ekle…", "Qovluq əlavə et…"),
    "Menu.StopWatching": ("Stop Watching", "停止监控", "Dejar de vigilar", "Cesser de surveiller",
                          "監視をやめる", "보기 중지", "Parar de observar",
                          "İzlemeyi durdur", "İzləməyi dayandır"),

    # keep / size
    "Menu.KeepOnLine": ("Keep on line", "绳上保留数量", "Mantener en la cuerda", "Garder sur la corde",
                        "ラインに保持", "줄에 유지", "Manter na corda", "Çizgide tut", "Xətdə saxla"),
    "Menu.KeepFit": ("As many as fit", "随屏宽自适应", "Tantas como quepan", "Autant que possible",
                     "画面に収まるだけ", "화면에 맞게", "Quantas couberem", "Sığdığı kadar",
                     "Yerləşdiyi qədər"),
    "Menu.KeepCount": ("{0} captures", "{0} 张", "{0} capturas", "{0} captures",
                       "{0} 枚", "{0}개", "{0} capturas", "{0} görüntü", "{0} çəkiliş"),
    "Menu.Size": ("Size", "尺寸", "Tamaño", "Taille", "サイズ", "크기", "Tamanho", "Boyut", "Ölçü"),
    "Menu.SizeSmall": ("Small", "小", "Pequeño", "Petit", "小", "작게", "Pequeno", "Küçük", "Kiçik"),
    "Menu.SizeMedium": ("Medium", "中", "Mediano", "Moyen", "中", "보통", "Médio", "Orta", "Orta"),
    "Menu.SizeLarge": ("Large", "大", "Grande", "Grand", "大", "크게", "Grande", "Büyük", "Böyük"),

    # shortcut
    "Menu.Shortcut": ("Shortcut", "快捷键", "Atajo", "Raccourci", "ショートカット",
                      "바로 가기", "Atalho", "Kısayol", "Qısayol"),
    "Menu.ShortcutNone": ("None", "无", "Ninguno", "Aucun", "なし", "없음", "Nenhum", "Yok", "Yoxdur"),
    "Menu.ChangeShortcut": ("Change Shortcut…", "更改快捷键…", "Cambiar atajo…", "Changer le raccourci…",
                            "ショートカットを変更…", "바로 가기 변경…", "Alterar atalho…",
                            "Kısayolu değiştir…", "Qısayolu dəyiş…"),

    # card context menu
    "Card.Copy": ("Copy", "复制", "Copiar", "Copier", "コピー", "복사", "Copiar", "Kopyala", "Kopyala"),
    "Card.Open": ("Open", "打开", "Abrir", "Ouvrir", "開く", "열기", "Abrir", "Aç", "Aç"),
    "Card.Preview": ("Preview", "预览", "Vista previa", "Aperçu", "プレビュー", "미리 보기",
                     "Pré-visualizar", "Önizleme", "Önizləmə"),
    "Card.Crop": ("Crop", "裁剪", "Recortar", "Rogner", "トリミング", "자르기", "Recortar",
                  "Kırp", "Kəs"),
    "Card.Annotate": ("Annotate", "标注", "Anotar", "Annoter", "注釈を付ける", "주석 달기",
                      "Anotar", "Açıklama ekle", "Şərh et"),
    "Card.ShowInExplorer": ("Show in Explorer", "在资源管理器中显示", "Mostrar en el Explorador",
                            "Afficher dans l'Explorateur", "エクスプローラーで表示",
                            "탐색기에서 보기", "Mostrar no Explorador",
                            "Gezgin'de göster", "Explorer-də göstər"),
    "Card.SaveToDesktop": ("Save to Desktop", "保存到桌面", "Guardar en el escritorio",
                           "Enregistrer sur le Bureau", "デスクトップに保存",
                           "바탕 화면에 저장", "Salvar na área de trabalho",
                           "Masaüstüne kaydet", "İş masasına yadda saxla"),
    "Card.Discard": ("Discard", "丢弃", "Descartar", "Jeter", "破棄", "버리기", "Descartar",
                     "At", "At"),
    "Card.TakeDown": ("Take down", "取下", "Bajar", "Décrocher", "降ろす", "내리기", "Tirar",
                      "İndir", "Endir"),
    "Card.Recycle": ("Move to Recycle Bin", "移到回收站", "Mover a la papelera",
                     "Mettre à la corbeille", "ごみ箱に移動", "휴지통으로 이동",
                     "Mover para a Lixeira", "Geri Dönüşüm Kutusu'na taşı",
                     "Zibil qutusuna köçür"),
    "Card.TakeDownLeft": ("Take down everything to the left", "取下左侧所有内容",
                          "Bajar todo lo que queda a la izquierda",
                          "Décrocher tout ce qui est à gauche",
                          "左側をすべて降ろす", "왼쪽의 모든 항목 내리기",
                          "Tirar tudo à esquerda", "Soldakilerin hepsini indir",
                          "Soldakıların hamısını yığışdır"),
    "Card.TakeDownRight": ("Take down everything to the right", "取下右侧所有内容",
                           "Bajar todo lo que queda a la derecha",
                           "Décrocher tout ce qui est à droite",
                           "右側をすべて降ろす", "오른쪽의 모든 항목 내리기",
                           "Tirar tudo à direita", "Sağdakilerin hepsini indir",
                           "Sağdakıların hamısını yığışdır"),
    "Card.Copied": ("Copied!", "已复制！", "¡Copiado!", "Copié !", "コピーしました",
                    "복사됨!", "Copiado!", "Kopyalandı!", "Kopyalandı!"),

    # the line itself
    "Line.Empty": ("Take a screenshot and it will hang here", "截一张图，它会挂到这里",
                   "Haz una captura y colgará aquí", "Prenez une capture, elle se pendra ici",
                   "スクリーンショットを撮るとここに掛かります", "스크린샷을 찍으면 여기에 걸립니다",
                   "Tire uma captura e ela ficará pendurada aqui",
                   "Ekran görüntüsü alın, buraya asılır", "Ekran görüntüsü alın, buraya asılacaq"),
    "Line.EmptyHint": ("Or copy an image — it hangs too", "或者复制一张图片，它也会挂上来",
                       "O copia una imagen: también cuelga", "Ou copiez une image, elle se pendra aussi",
                       "画像をコピーしても掛かります", "이미지를 복사해도 걸립니다",
                       "Ou copie uma imagem — ela também fica na corda",
                       "Ya da bir görüntü kopyalayın, o da asılır",
                       "Və ya bir şəkil kopyalayın, o da asılır"),

    # crop window
    "Crop.Title": ("Crop", "裁剪", "Recortar", "Rogner", "トリミング", "자르기", "Recortar",
                   "Kırpma", "Kəsmə"),
    "Crop.Cancel": ("Cancel", "取消", "Cancelar", "Annuler", "キャンセル", "취소", "Cancelar",
                    "İptal", "Ləğv et"),
    "Crop.Apply": ("Apply crop", "应用裁剪", "Aplicar recorte", "Appliquer", "適用", "적용",
                   "Aplicar recorte", "Kırpmayı uygula", "Kəsməni tətbiq et"),
    "Crop.Size": ("{0} × {1}", "{0} × {1}", "{0} × {1}", "{0} × {1}", "{0} × {1}",
                  "{0} × {1}", "{0} × {1}", "{0} × {1}", "{0} × {1}"),

    # settings pages
    "Settings.Title": ("Settings", "设置", "Ajustes", "Paramètres", "設定", "설정",
                       "Configurações", "Ayarlar", "Parametrlər"),
    "Settings.General": ("General", "常规", "General", "Général", "一般", "일반",
                         "Geral", "Genel", "Ümumi"),
    "Settings.GeneralDesc": ("Language, sounds and how much the line holds",
                             "语言、声音与绳上容量", "Idioma, sonidos y cuántas fotos guarda la cuerda",
                             "Langue, sons et nombre de captures sur la corde",
                             "言語、サウンド、ラインに保持する枚数", "언어, 소리, 줄에 보관할 개수",
                             "Idioma, sons e quantas capturas a corda guarda",
                             "Dil, sesler ve çizginin tutacağı sayı", "Dil, səslər və xətdə saxlanacaq miqdar"),
    "Settings.Language": ("Language", "语言", "Idioma", "Langue", "言語", "언어",
                          "Idioma", "Dil", "Dil"),
    "Settings.LanguageDesc": ("Pegged speaks ten languages", "Pegged 支持 10 种语言",
                              "Pegged habla diez idiomas", "Pegged parle dix langues",
                              "Pegged は 10 言語に対応", "Pegged는 10개 언어를 지원합니다",
                              "O Pegged fala dez idiomas", "Pegged on dil konuşur",
                              "Pegged on dildə danışır"),
    "Settings.Sounds": ("Sounds", "声音", "Sonidos", "Sons", "サウンド", "소리",
                        "Sons", "Sesler", "Səslər"),
    "Settings.SoundsDesc": ("A soft sound when a capture hangs or comes down",
                            "挂上或取下时的轻柔提示音", "Un sonido suave al colgar o bajar una captura",
                            "Un son léger quand une capture est accrochée ou décrochée",
                            "キャプチャを掛けたり降ろしたりしたときに小さな音",
                            "캡처를 걸거나 내릴 때 부드러운 소리",
                            "Um som suave quando uma captura é pendurada ou retirada",
                            "Bir görüntü asıldığında veya indiğinde yumuşak ses",
                            "Çəkiliş asılanda və ya endiriləndə yumşaq səs"),
    "Settings.Keep": ("Keep on line", "绳上保留数量", "Mantener en la cuerda", "Garder sur la corde",
                      "ラインに保持", "줄에 유지", "Manter na corda", "Çizgide tut", "Xətdə saxla"),
    "Settings.KeepDesc": ("Older captures fall off the end", "更早的会自动脱落",
                          "Las capturas más antiguas caen por el final",
                          "Les captures plus anciennes tombent au bout",
                          "古いキャプチャは端から落ちます", "오래된 캡처는 끝에서 떨어집니다",
                          "As capturas mais antigas caem do fim",
                          "Eski görüntüler sondan düşer", "Köhnə çəkilişlər sonundan düşür"),
    "Settings.Size": ("Card size", "卡片尺寸", "Tamaño de tarjeta", "Taille des cartes",
                      "カードサイズ", "카드 크기", "Tamanho do cartão", "Kart boyutu", "Kart ölçüsü"),
    "Settings.SizeDesc": ("How big captures hang", "卡片挂多大", "De qué tamaño cuelgan las capturas",
                          "Taille à laquelle les captures sont accrochées",
                          "キャプチャを掛けるサイズ", "캡처가 걸리는 크기",
                          "De que tamanho as capturas ficam", "Görüntülerin asılma boyutu",
                          "Çəkilişlərin asılma ölçüsü"),
    "Settings.Capture": ("Capture sources", "捕获源", "Orígenes de captura", "Sources des captures",
                         "キャプチャ元", "캡처 소스", "Origens das capturas", "Yakalama kaynakları",
                         "Çəkiliş mənbələri"),
    "Settings.CaptureDesc": ("Where Pegged looks for new captures", "Pegged 从哪里发现新截图",
                             "Dónde busca Pegged nuevas capturas", "Où Pegged cherche de nouvelles captures",
                             "Pegged が新しいキャプチャを探す場所", "Pegged가 새 캡처를 찾는 위치",
                             "Onde o Pegged procura novas capturas",
                             "Pegged'in yeni görüntüleri aradığı yer", "Pegged-in yeni çəkilişləri axtardığı yer"),
    "Settings.Folder": ("Screenshots folder", "截图文件夹", "Carpeta de capturas",
                        "Dossier des captures", "スクリーンショットフォルダ",
                        "스크린샷 폴더", "Pasta de capturas", "Ekran görüntüsü klasörü",
                        "Ekran görüntüsü qovluğu"),
    "Settings.FolderDesc": ("Watched for new images", "监控此目录的新图片",
                            "Vigilada en busca de imágenes nuevas",
                            "Surveillé à la recherche de nouvelles images",
                            "新しい画像を監視します", "새 이미지를 위해 관찰됨",
                            "Observada em busca de novas imagens",
                            "Yeni görüntüler için izleniyor", "Yeni şəkillər üçün izlənilir"),
    "Settings.FolderChange": ("Change…", "更改…", "Cambiar…", "Changer…", "変更…", "변경…",
                              "Alterar…", "Değiştir…", "Dəyiş…"),
    "Settings.Extra": ("Also watch folders", "同时监控文件夹", "Carpetas que también vigilar",
                       "Dossiers également surveillés", "監視するフォルダ", "함께 볼 폴더",
                       "Pastas que também observar", "Ayrıca izlenecek klasörler",
                       "Əlavə izlənilən qovluqlar"),
    "Settings.ExtraDesc": ("Up to three folders; new images in them hang too",
                           "最多三个文件夹，其中的新图片也会挂上",
                           "Hasta tres carpetas; sus imágenes nuevas también cuelgan",
                           "Jusqu'à trois dossiers ; leurs nouvelles images sont aussi accrochées",
                           "最大 3 フォルダ。新しい画像も掛かります",
                           "최대 세 폴더; 새 이미지도 함께 걸립니다",
                           "Até três pastas; novas imagens nelas também ficam na corda",
                           "Üç klasöre kadar; içindeki yeni görüntüler de asılır",
                           "Üç qovluğa qədər; oradakı yeni şəkillər də asılır"),
    "Settings.Clipboard": ("Hang copied images", "挂上复制的图片", "Colgar imágenes copiadas",
                           "Accrocher les images copiées", "コピーした画像を掛ける",
                           "복사한 이미지 걸기", "Pendurar imagens copiadas",
                           "Kopyalanan görüntüleri as", "Kopyalanmış şəkilləri as"),
    "Settings.ClipboardDesc": ("Images copied from any app land on the line",
                               "任何应用中复制的图片都会挂到绳上",
                               "Las imágenes copiadas desde cualquier app cuelgan de la cuerda",
                               "Les images copiées depuis n'importe quelle app se pendent à la corde",
                               "どのアプリでコピーした画像もラインに掛かります",
                               "어느 앱에서 복사한 이미지도 줄에 걸립니다",
                               "Imagens copiadas de qualquer app vão para a corda",
                               "Herhangi bir uygulamadan kopyalanan görüntüler çizgiye asılır",
                               "İstənilən tətbiqdən kopyalanmış şəkillər xəttə asılır"),
    "Settings.Appearance": ("Appearance & displays", "外观与显示器", "Apariencia y pantallas",
                            "Apparence et écrans", "外観とディスプレイ", "모양 및 디스플레이",
                            "Aparência e telas", "Görünüm ve ekranlar", "Görünüş və ekranlar"),
    "Settings.AppearanceDesc": ("Theme, timing and which screen the line uses",
                                "主题、时机与挂绳所在屏幕", "Tema, tiempos y en qué pantalla está la cuerda",
                                "Thème, délais et écran utilisé par la corde",
                                "テーマ、タイミング、ラインを表示する画面",
                                "테마, 타이밍, 줄을 사용할 화면",
                                "Tema, tempos e em qual tela fica a corda",
                                "Tema, zamanlama ve çizginin kullanacağı ekran",
                                "Mövzu, vaxt və xəttin istifadə edəcəyi ekran"),
    "Settings.Theme": ("Theme", "主题", "Tema", "Thème", "テーマ", "테마", "Tema", "Tema", "Mövzu"),
    "Settings.ThemeAuto": ("Follow Windows", "跟随 Windows", "Seguir a Windows", "Suivre Windows",
                           "Windows に合わせる", "Windows를 따름", "Seguir o Windows",
                           "Windows'u izle", "Windows-a uyğun"),
    "Settings.ThemeLight": ("Light", "浅色", "Claro", "Clair", "ライト", "밝게", "Claro",
                            "Açık", "Açıq"),
    "Settings.ThemeDark": ("Dark", "深色", "Oscuro", "Sombre", "ダーク", "어둡게", "Escuro",
                           "Koyu", "Tünd"),
    "Settings.RevealDelay": ("Hover delay", "悬停停顿时长", "Tiempo de espera", "Délai de survol",
                             "ホバー待ち時間", "호버 지연", "Tempo de espera", "Bekleme süresi",
                             "Gözləmə müddəti"),
    "Settings.RevealDelayDesc": ("How long the pointer rests before the line comes down",
                                 "指针需停留多久挂绳才落下",
                                 "Cuánto debe detenerse el puntero para que baje la cuerda",
                                 "Temps d'arrêt du pointeur avant que la corde descende",
                                 "ポインターが止まってからラインが下りるまでの時間",
                                 "포인터가 멈춘 후 줄이 내려오는 시간",
                                 "Quanto tempo o ponteiro para antes da corda descer",
                                 "Çizginin inmesi için işaretçinin bekleme süresi",
                                 "Xəttin enməsi üçün göstəricinin gözləmə müddəti"),
    "Settings.ExtraFolders": ("Extra folders", "额外监控文件夹", "Carpetas adicionales",
                             "Dossiers supplémentaires", "追加フォルダー", "추가 폴더",
                             "Pastas adicionais", "Ek klasörler", "Əlavə qovluqlar"),
    "Settings.ExtraFoldersDesc": ("Up to three more folders are watched for new captures",
                                  "最多再监视 3 个文件夹里的新截图",
                                  "Se vigilan hasta tres carpetas más en busca de capturas",
                                  "Jusqu'à trois dossiers de plus sont surveillés",
                                  "最大3つまで追加フォルダーを監視します",
                                  "최대 3개 폴더를 추가로 감시합니다",
                                  "Até três pastas adicionais são monitoradas",
                                  "En fazla üç klasör daha izlenir",
                                  "Ən çoxu üç əlavə qovluq izlənir"),
    "Settings.ExtraFoldersNone": ("None", "无", "Ninguna", "Aucun", "なし", "없음",
                                  "Nenhuma", "Yok", "Yoxdur"),
    "Settings.ExtraFoldersFull": ("Three extra folders is the maximum. Remove one first.",
                                  "最多只能添加 3 个额外文件夹，请先移除一个。",
                                  "Tres carpetas adicionales es el máximo. Quita una primero.",
                                  "Trois dossiers supplémentaires au maximum. Retires-en un.",
                                  "追加フォルダーは3つまでです。先に1つ削除してください。",
                                  "추가 폴더는 최대 3개입니다. 먼저 하나를 제거하세요.",
                                  "Três pastas adicionais é o máximo. Remova uma antes.",
                                  "En fazla üç ek klasör. Önce birini kaldırın.",
                                  "Ən çoxu üç əlavə qovluq. Əvvəlcə birini silin."),
    "Settings.FolderAdd": ("Add folder", "添加文件夹", "Añadir carpeta", "Ajouter un dossier",
                           "フォルダーを追加", "폴더 추가", "Adicionar pasta",
                           "Klasör ekle", "Qovluq əlavə et"),
    "Settings.FolderClear": ("Clear", "清空", "Vaciar", "Vider", "クリア", "지우기",
                             "Limpar", "Temizle", "Təmizlə"),
    "Settings.Inbox": ("Inbox mode", "Inbox 模式", "Modo bandeja", "Mode boîte de réception",
                       "受信箱モード", "받은편지함 모드", "Modo caixa de entrada",
                       "Gelen kutusu modu", "Gələn qutusu rejimi"),
    "Settings.InboxDesc": ("Windows saves captures straight into Pegged's own folder",
                           "Windows 的截图直接存进 Pegged 自己的文件夹",
                           "Windows guarda las capturas en la carpeta de Pegged",
                           "Windows enregistre les captures dans le dossier de Pegged",
                           "Windows のキャプチャを Pegged のフォルダーへ直接保存します",
                           "Windows 캡처를 Pegged 폴더에 바로 저장합니다",
                           "O Windows guarda as capturas na pasta do Pegged",
                           "Windows görüntüleri doğrudan Pegged klasörüne kaydeder",
                           "Windows çəkilişləri birbaşa Pegged qovluğuna yazır"),
    "Settings.InboxRestore": ("Restore location", "还原位置", "Restaurar ubicación",
                              "Rétablir l'emplacement", "場所を戻す", "위치 복원",
                              "Restaurar local", "Konumu geri yükle", "Yeri bərpa et"),
    "Settings.InboxPath": ("Windows saves screenshots in: {0}",
                           "Windows 当前保存截图的位置：{0}",
                           "Windows guarda las capturas en: {0}",
                           "Windows enregistre les captures dans : {0}",
                           "Windows のスクリーンショット保存先: {0}",
                           "Windows 스크린샷 저장 위치: {0}",
                           "O Windows guarda as capturas em: {0}",
                           "Windows ekran görüntülerini şuraya kaydeder: {0}",
                           "Windows ekran görüntülərini buraya yazır: {0}"),
    "Settings.InboxFailed": ("Windows would not change the screenshot folder, so inbox mode is off.",
                             "Windows 不允许修改截图文件夹，Inbox 模式已关闭。",
                             "Windows no permitió cambiar la carpeta de capturas; el modo bandeja está desactivado.",
                             "Windows a refusé de changer le dossier des captures : le mode est désactivé.",
                             "Windows がフォルダー変更を拒否したため、受信箱モードはオフです。",
                             "Windows가 폴더 변경을 허용하지 않아 받은편지함 모드가 꺼졌습니다.",
                             "O Windows não permitiu mudar a pasta; o modo caixa de entrada está desligado.",
                             "Windows klasörü değiştirmedi; gelen kutusu modu kapalı.",
                             "Windows qovluğu dəyişmədi; gələn qutusu rejimi söndürüldü."),
    "Settings.License": ("Ported from tendedero (MIT). Pegged is MIT licensed.",
                         "移植自 tendedero（MIT），Pegged 以 MIT 协议开源。",
                         "Portado de tendedero (MIT). Pegged tiene licencia MIT.",
                         "Porté depuis tendedero (MIT). Pegged est sous licence MIT.",
                         "tendedero (MIT) からの移植。Pegged は MIT ライセンスです。",
                         "tendedero(MIT)에서 이식했습니다. Pegged는 MIT 라이선스입니다.",
                         "Portado do tendedero (MIT). Pegged tem licença MIT.",
                         "tendedero (MIT) uyarlaması. Pegged MIT lisanslıdır.",
                         "tendedero (MIT) əsasında. Pegged MIT lisenziyalıdır."),
    "Settings.TopEdge": ("Reveal at the top edge", "在屏幕顶边唤出", "Mostrar en el borde superior",
                         "Afficher au bord supérieur", "上端で表示", "위쪽 가장자리에서 표시",
                         "Mostrar na borda superior", "Üst kenarda göster", "Yuxarı kənarda göstər"),
    "Settings.TopEdgeDesc": ("Resting the pointer at the top of a screen brings the line down",
                             "指针停在屏幕顶部时挂绳落下",
                             "Detener el puntero en la parte superior de una pantalla baja la cuerda",
                             "Arrêter le pointeur en haut d'un écran fait descendre la corde",
                             "画面上部にポインターを止めるとラインが下ります",
                             "화면 위쪽에 포인터를 두면 줄이 내려옵니다",
                             "Parar o ponteiro no topo de uma tela faz a corda descer",
                             "İşaretçiyi ekranın üstünde bırakmak çizgiyi indirir",
                             "Göstəricini ekranın yuxarısında saxlamaq xətti endirir"),
    "Settings.Peek": ("Reveal on a new capture", "有新截图时唤出", "Mostrar con una captura nueva",
                      "Afficher à chaque nouvelle capture", "新しいキャプチャで表示",
                      "새 캡처에서 표시", "Mostrar numa nova captura",
                      "Yeni görüntüde göster", "Yeni çəkilişdə göstər"),
    "Settings.PeekDesc": ("A new capture shows the line for a moment", "新截图会让挂绳短暂落下",
                          "Una captura nueva muestra la cuerda un momento",
                          "Une nouvelle capture montre la corde un instant",
                          "新しいキャプチャがあると一瞬ラインが下ります",
                          "새 캡처가 있으면 줄이 잠시 보입니다",
                          "Uma nova captura mostra a corda por um momento",
                          "Yeni bir görüntü çizgiyi bir an gösterir",
                          "Yeni çəkiliş xətti bir an göstərir"),
    "Settings.OverFullScreen": ("Over full screen apps", "在全屏应用之上",
                                "Sobre apps en pantalla completa", "Au-dessus des apps plein écran",
                                "全画面アプリの上に", "전체 화면 앱 위에",
                                "Sobre apps em tela cheia", "Tam ekran uygulamaların üzerinde",
                                "Tam ekran tətbiqlərin üzərində"),
    "Settings.OverFullScreenDesc": ("The line comes down even when an app fills the screen",
                                    "应用铺满屏幕时挂绳依然落下",
                                    "La cuerda baja incluso cuando una app llena la pantalla",
                                    "La corde descend même quand une app remplit l'écran",
                                    "アプリが画面いっぱいでもラインは下ります",
                                    "앱이 화면을 채워도 줄이 내려옵니다",
                                    "A corda desce mesmo quando um app preenche a tela",
                                    "Bir uygulama ekranı doldursa da çizgi iner",
                                    "Tətbiq ekranı doldursa da xətt enir"),
    "Settings.Shortcuts": ("Shortcuts", "快捷键", "Atajos", "Raccourcis", "ショートカット",
                           "바로 가기", "Atalhos", "Kısayollar", "Qısayollar"),
    "Settings.ShortcutsDesc": ("Keyboard bindings", "键盘绑定", "Combinaciones de teclas",
                               "Combinaisons de touches", "キー割り当て", "키 바인딩",
                               "Combinações de teclas", "Klavye bağlamaları", "Klavye təyinatları"),
    "Settings.ShortcutToggle": ("Show or hide the line", "显示或隐藏挂绳",
                                "Mostrar u ocultar la cuerda", "Afficher ou masquer la corde",
                                "ラインの表示・非表示", "줄 표시 또는 숨기기",
                                "Mostrar ou ocultar a corda", "Çizgiyi göster veya gizle",
                                "Xətti göstər və ya gizlət"),
    "Settings.Record": ("Press the new shortcut", "按下新的快捷键", "Pulsa el nuevo atajo",
                        "Appuyez sur le nouveau raccourci", "新しいショートカットを押してください",
                        "새 바로 가기를 누르세요", "Pressione o novo atalho",
                        "Yeni kısayola basın", "Yeni qısayolu basın"),
    "Settings.RecordHint": ("Esc cancels, Delete removes the shortcut.",
                            "Esc 取消，Delete 清除快捷键。",
                            "Esc cancela y Supr elimina el atajo.",
                            "Échap annule, Suppr supprime le raccourci.",
                            "Esc でキャンセル、Delete で解除します。",
                            "Esc 취소, Delete는 바로 가기 제거.",
                            "Esc cancela, Delete remove o atalho.",
                            "Esc iptal eder, Delete kısayolu kaldırır.",
                            "Esc ləğv edir, Delete qısayolu silir."),
    "Settings.About": ("About", "关于", "Acerca de", "À propos", "このアプリについて",
                       "정보", "Sobre", "Hakkında", "Haqqında"),
    "Settings.AboutDesc": ("Version, licence and where this came from", "版本、许可与来源",
                           "Versión, licencia y origen", "Version, licence et origine",
                           "バージョン、ライセンス、由来", "버전, 라이선스 및 출처",
                           "Versão, licença e origem", "Sürüm, lisans ve köken",
                           "Versiya, lisenziya və mənşə"),
    "Settings.Version": ("Version {0}", "版本 {0}", "Versión {0}", "Version {0}",
                         "バージョン {0}", "버전 {0}", "Versão {0}", "Sürüm {0}", "Versiya {0}"),
    "Settings.Website": ("Website", "网站", "Sitio web", "Site web", "ウェブサイト",
                         "웹사이트", "Site", "Web sitesi", "Veb-sayt"),
    "Settings.Credits": ("A native Windows port of tendedero for macOS by Alejandro Bujan, "
                         "released under the MIT licence.",
                         "原生 Windows 版本，移植自 Alejandro Bujan 的 macOS 应用 tendedero，"
                         "遵循 MIT 许可发布。",
                         "Versión nativa para Windows de tendedero para macOS, de Alejandro Bujan, "
                         "publicada bajo la licencia MIT.",
                         "Portage natif Windows de tendedero pour macOS par Alejandro Bujan, "
                         "publié sous licence MIT.",
                         "Alejandro Bujan 氏の macOS アプリ tendedero のネイティブ Windows 版"
                         "（MIT ライセンス）。",
                         "Alejandro Bujan의 macOS용 tendedero의 네이티브 Windows 이식판(MIT 라이선스).",
                         "Porte nativo para Windows do tendedero para macOS, de Alejandro Bujan, "
                         "publicado sob a licença MIT.",
                         "Alejandro Bujan'ın macOS için tendedero uygulamasının yerel Windows sürümü, "
                         "MIT lisansıyla.",
                         "Alejandro Bujan-ın macOS üçün tendedero tətbiqinin yerli Windows versiyası, "
                         "MIT lisenziyası ilə."),

    # dialogs
    "Dialog.WatchFolderPrompt": ("New images saved to this folder will hang on the line.",
                                 "保存到此文件夹的新图片会挂到绳上。",
                                 "Las imágenes nuevas guardadas en esta carpeta colgarán de la cuerda.",
                                 "Les nouvelles images de ce dossier seront accrochées à la corde.",
                                 "このフォルダに保存された新しい画像はラインに掛かります。",
                                 "이 폴더에 저장된 새 이미지는 줄에 걸립니다.",
                                 "Novas imagens salvas nesta pasta ficarão na corda.",
                                 "Bu klasöre kaydedilen yeni görüntüler çizgiye asılır.",
                                 "Bu qovluğa yadda saxlanılan yeni şəkillər xəttə asılacaq."),
    "Dialog.Watch": ("Watch", "监控", "Vigilar", "Surveiller", "監視", "보기", "Observar",
                     "İzle", "İzlə"),
    "Dialog.LoginFailed": ("Could not change the login setting", "无法更改开机自启设置",
                           "No se pudo cambiar el ajuste de inicio",
                           "Impossible de modifier le réglage de connexion",
                           "ログイン設定を変更できませんでした", "로그인 설정을 변경할 수 없습니다",
                           "Não foi possível alterar a configuração de entrada",
                           "Oturum açma ayarı değiştirilemedi", "Giriş parametri dəyişdirilmədi"),
    "Dialog.LoginFailedBody": ("Try running Pegged from a folder you can write to.",
                               "请从可写入的文件夹运行 Pegged。",
                               "Prueba a ejecutar Pegged desde una carpeta con permiso de escritura.",
                               "Lancez Pegged depuis un dossier accessible en écriture.",
                               "書き込み可能なフォルダから Pegged を実行してください。",
                               "쓰기 권한이 있는 폴더에서 Pegged를 실행해 보세요.",
                               "Tente executar o Pegged a partir de uma pasta com permissão de escrita.",
                               "Pegged'i yazma izni olan bir klasörden çalıştırmayı deneyin.",
                               "Pegged-i yazma icazəsi olan qovluqdan işə salın."),
    "Dialog.CropFailed": ("Could not crop this image", "无法裁剪这张图片",
                          "No se pudo recortar esta imagen", "Impossible de rogner cette image",
                          "この画像をトリミングできませんでした", "이 이미지를 자를 수 없습니다",
                          "Não foi possível recortar esta imagem",
                          "Bu görüntü kırpılamadı", "Bu şəkil kəsilmədi"),
    "Dialog.TrashFailed": ("Could not move this file to the Recycle Bin",
                           "无法将此文件移到回收站",
                           "No se pudo mover este archivo a la papelera",
                           "Impossible de mettre ce fichier à la corbeille",
                           "このファイルをごみ箱に移動できませんでした",
                           "이 파일을 휴지통으로 옮길 수 없습니다",
                           "Não foi possível mover este arquivo para a Lixeira",
                           "Bu dosya Geri Dönüşüm Kutusu'na taşınamadı",
                           "Bu fayl zibil qutusuna köçürülmədi"),
    "Dialog.ClipboardOffered": ("Let Pegged hang the images you copy?",
                                "让 Pegged 挂上你复制的图片？",
                                "¿Quieres que Pegged cuelgue las imágenes que copies?",
                                "Voulez-vous que Pegged accroche les images que vous copiez ?",
                                "コピーした画像を Pegged に掛けますか？",
                                "복사한 이미지를 Pegged에 걸까요?",
                                "Quer que o Pegged pendure as imagens que você copia?",
                                "Kopyaladığınız görüntüleri Pegged assın mı?",
                                "Kopyaladığınız şəkilləri Pegged assın?"),
}

HEADER = '''<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:import namespace="http://www.w3.org/XML/1998/namespace" />
    <xsd:element name="root" msdata:IsDataSet="true">
      <xsd:complexType>
        <xsd:choice maxOccurs="unbounded">
          <xsd:element name="metadata">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" />
              </xsd:sequence>
              <xsd:attribute name="name" use="required" type="xsd:string" />
              <xsd:attribute name="type" type="xsd:string" />
              <xsd:attribute name="mimetype" type="xsd:string" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="assembly">
            <xsd:complexType>
              <xsd:attribute name="alias" type="xsd:string" />
              <xsd:attribute name="name" type="xsd:string" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="data">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
                <xsd:element name="comment" type="xsd:string" minOccurs="0" msdata:Ordinal="2" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" msdata:Ordinal="1" />
              <xsd:attribute name="type" type="xsd:string" msdata:Ordinal="3" />
              <xsd:attribute name="mimetype" type="xsd:string" msdata:Ordinal="4" />
              <xsd:attribute ref="xml:space" />
            </xsd:complexType>
          </xsd:element>
          <xsd:element name="resheader">
            <xsd:complexType>
              <xsd:sequence>
                <xsd:element name="value" type="xsd:string" minOccurs="0" msdata:Ordinal="1" />
              </xsd:sequence>
              <xsd:attribute name="name" type="xsd:string" use="required" />
            </xsd:complexType>
          </xsd:element>
        </xsd:choice>
      </xsd:complexType>
    </xsd:element>
  </xsd:schema>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
'''


def write_resx(path, pairs):
    body = [HEADER]
    for key, value in pairs:
        body.append('  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n'
                    % (su.escape(key), su.escape(value)))
    body.append('</root>\n')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        f.write("".join(body))


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, "src", "Pegged", "Resources")
    write_resx(os.path.join(out, "Strings.resx"),
               [(k, v[0]) for k, v in T.items()])
    for i, lang in enumerate(LANGUAGES, start=1):
        write_resx(os.path.join(out, f"Strings.{lang}.resx"),
                   [(k, v[i]) for k, v in T.items()])
    print(f"wrote {len(LANGUAGES) + 1} resource files, {len(T)} strings each")


if __name__ == "__main__":
    main()
