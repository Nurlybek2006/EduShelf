# EduShelf — электрондық білім беру ресурстары

Қазақ тіліндегі ASP.NET Core MVC кітапханасы. Студент материалдарды іздейді, PDF қарайды, жүктейді және таңдаулыға сақтайды. Оқытушы материал қосады, өңдейді, жариялайды және архивтейді. Әкімші материалдарды, рөлдерді және анықтамалықтарды басқарады.

Бұл — бірінші кезең. Нақты AI провайдері қосылмаған; интерфейс «Талдау орындалмаған» күйін көрсетеді. Дайындық пен тексерудің нақты нәтижелері [PHASE-1-STATUS.md](PHASE-1-STATUS.md) ішінде, кодтың түсіндірмесі [STUDY.md](STUDY.md) ішінде.

## Технологиялар және орта

| Бөлік | Нұсқа / шешім |
| --- | --- |
| Платформа | .NET 10 LTS, `net10.0` |
| Осы ортада анықталған SDK | `10.0.401` |
| Осы ортада анықталған runtime | `10.0.12` |
| MVC және беттер | ASP.NET Core MVC, Razor, HTML/CSS/JavaScript |
| Деректер және аккаунттар | EF Core SQL Server және ASP.NET Core Identity `10.0.13` |
| EF CLI | Жобаға бекітілген `dotnet-ef 10.0.13` |
| Деректер базасы | Microsoft SQL Server; басқа provider қолданылмайды |

2026 жылғы 20 қазанда тексерілген [Microsoft қолдау саясаты](https://dotnet.microsoft.com/en-us/platform/support/policy) бойынша .NET 10 — LTS, қолдау мерзімі 2028 жылғы 14 қарашаға дейін. Ресми ағымдағы patch — `10.0.13`. Осы компьютердегі `10.0.12` runtime нұсқасын жариялау алдында қолдаудағы соңғы patch нұсқасына жаңартыңыз.

Осы ортада **`MSSQLSERVER` default instance**, SQL Server нұсқасы **`16.00.1000`**, қосылу мекенжайы **`.`** тексерілді. `SQLEXPRESS` бар деп болжанбайды: төмендегі нұсқа тек сол instance бөлек орнатылған ортаға арналған.

SSMS 21 — базаны басқару клиенті. Оның орнатылуы SQL Server қозғалтқышының орнатылғанын немесе іске қосылғанын білдірмейді.

## Жылдам іске қосу

PowerShell терезесін жоба каталогында ашыңыз:

```powershell
dotnet --info
dotnet restore
dotnet tool restore
dotnet build --no-restore
```

Содан кейін төменде берілген **миграция** немесе **SSMS SQL скрипті** жолының бірін таңдаңыз. Демо деректер қажет болса, бөлек seed орындаңыз. Қосымшаны іске қосу:

```powershell
dotnet run --launch-profile http
```

Браузерде [http://localhost:5046](http://localhost:5046) ашыңыз. HTTPS профилі: `dotnet run --launch-profile https`, мекенжайы `https://localhost:7209`; жергілікті HTTPS үшін .NET development certificate бапталған болуы керек.

Қалыпты web іске қосылу базаны автоматты түрде өзгертпейді және демо аккаунт жасамайды. `--check-db`, `--migrate`, `--seed` — қызметтік әрекеттер: орындалып, процесс аяқталады.

## SQL Server қосылымы

Қосылым кілті — `ConnectionStrings:DefaultConnection`. Коммиттелген `appsettings.json` жергілікті Windows Authentication мысалын қамтиды; онда пароль жоқ. Өз ортаңызға арналған мәнді user-secrets немесе environment variable арқылы үстінен орнатыңыз.

**Жергілікті default instance:**

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=EduShelf;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
```

**SQLEXPRESS бөлек орнатылған болса ғана:**

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.\SQLEXPRESS;Database=EduShelf;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
```

Environment variable баламасы:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=.;Database=EduShelf;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
```

Бұл айнымалы ағымдағы PowerShell процесіне және одан іске қосылған қосымшаға әсер етеді. Windows Authentication кезінде қосымшаны іске қосқан Windows пайдаланушысына SQL Server ішінде тиісті құқықтар берілуі керек. `TrustServerCertificate=True` мысалы жергілікті ортаға арналған; жарияланған ортада сенімді сервер сертификатын және `TrustServerCertificate=False` қолданыңыз.

Қосылымды тексеру:

База әлі жасалмаса, алдымен келесі бөлімдегі миграция немесе SQL скрипті арқылы схеманы дайындаңыз.

```powershell
dotnet run --launch-profile http -- --check-db
```

Қате болса: SQL Server қызметінің жұмысын, instance атауын, Windows login рұқсатын және көрсетілген базаға қолжетімділікті тексеріңіз. SSMS ішіндегі қосылым мен қосымшаның қосылым жолы бір серверге бағытталуы керек. SQL Server қолжетімсіз болса, SQLite-қа ауысу қарастырылмаған.

## Схеманы дайындау: екі баламалы жол

`Data/Migrations` және [scripts/EduShelf.sql](scripts/EduShelf.sql) бір EF схемасынан алынған. Скрипт `__EFMigrationsHistory` кестесін тексеріп, жетіспейтін миграцияларды ғана орындайды. Базаны дайындаудың бір жолын таңдаңыз; әр іске қосуда екеуін қатар орындау қажет емес. EF тәсілдері туралы [Microsoft құжаттамасы](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

### A. EF миграциясы

```powershell
dotnet tool restore
dotnet ef database update
```

Немесе дәл сол миграцияларды қолданбаның қызметтік командасымен орындаңыз:

```powershell
dotnet run --launch-profile http -- --migrate
```

База әлі жоқ болса, оны жасауға құқығы бар Windows login қажет. Құқық жеткіліксіз болса, әкімші базаны алдын ала жасап, migration орындаушысына схема өзгерту құқығын береді.

### B. SSMS 21 және дайын idempotent SQL

1. SSMS 21 ішінен **Database Engine**, **Server name: `.`**, **Windows Authentication** таңдап қосылыңыз. Өз ортаңызда басқа instance болса, тексерілген нақты атауын қолданыңыз.
2. Object Explorer → Databases → New Database арқылы `EduShelf` базасын жасаңыз. Егер ол бар болса, сол базаны таңдаңыз.
3. [scripts/EduShelf.sql](scripts/EduShelf.sql) файлын SSMS ішінде ашыңыз. Query терезесіндегі database тізімінен **EduShelf** таңдаңыз.
4. Скрипттің алдына төмендегі екі жолды қосып, дұрыс база таңдалғанын тексеріңіз; кейін скриптті орындаңыз:

```sql
USE [EduShelf];
GO
```

Скрипттің өзі **базаны жасамайды**. Оны `master` базасында орындамаңыз. Өзге атау таңдасаңыз, `USE` және `DefaultConnection` екеуін де сол атауға сәйкестендіріңіз.

Миграция өзгергенде SQL артефактын қайта жасау:

```powershell
dotnet ef migrations script --idempotent --output scripts/EduShelf.sql
```

## Демо деректер және аккаунттар

`--seed` рөлдер мен анықтамалықтарды толтырады. Демо аккаунттар мен алты оқу PDF-ы тек **Development** ортасында және `Seed:DemoEnabled=true` болғанда қосылады. Демо құпиясөздер репозиторийде сақталмайды.

Алғашқы баптау мысалы; `<...>` мәндерін өзіңіздің мәндеріңізбен ауыстырыңыз:

```powershell
dotnet user-secrets set "Seed:DemoEnabled" "true"
dotnet user-secrets set "Seed:AdminEmail" "admin@edushelf.local"
dotnet user-secrets set "Seed:AdminPassword" "<ӘКІМШІНІҢ_ҚҰПИЯСӨЗІ>"
dotnet user-secrets set "Seed:TeacherEmail" "teacher@edushelf.local"
dotnet user-secrets set "Seed:TeacherPassword" "<ОҚЫТУШЫНЫҢ_ҚҰПИЯСӨЗІ>"
dotnet user-secrets set "Seed:StudentEmail" "student@edushelf.local"
dotnet user-secrets set "Seed:StudentPassword" "<СТУДЕНТТІҢ_ҚҰПИЯСӨЗІ>"
dotnet run --launch-profile http -- --seed
```

Үш email бір-бірінен бөлек болуы керек. Құпиясөз талабы: кемінде 8 таңба, бас және кіші латын әрпі, сан және арнайы таңба. Осы жұмыс барысында жергілікті демо деректер бапталған болса, олардың конфигурациясын өз компьютеріңізде `dotnet user-secrets list` арқылы жеке көре аласыз. Бұл команданың нәтижесінде құпиясөздер де көрсетіледі; нәтижені Git-ке немесе жалпы құжатқа көшірмеңіз.

Seed қайта орындалса, аккаунттар мен материалдар қайталанбайды, бар материалдың редакциялық өзгерістері сақталады. Seed **бар аккаунттың құпиясөзін ауыстырмайды**. Password конфигурациясын өзгерту бұрын жасалған аккаунттың кіру деректерін өзгертпейді. Жаңа таза демо базаға seed жасағанда жаңа конфигурация қолданылады. Құпиясөзді қалпына келтіру интерфейсі бірінші кезеңге кірмейді.

Демо жинақ: сызықтық теңдеулер, үшбұрыштың ауданы, бірқалыпты қозғалыс, алгоритм, мәтіннің негізгі ойы және өсімдік жасушасы. Соңғы материал тек тіркелген пайдаланушыларға арналған; қалған бесеуі жария. Файлдар [DemoFiles](DemoFiles) ішінде, олардың генераторы [scripts/generate-demo-pdfs.py](scripts/generate-demo-pdfs.py). Web қосымшасын іске қосуға Python қажет емес. Seed жалған қаралым, жүктеу немесе AI нәтижесін қоспайды.

### Production әкімшісін бастапқы жасау

Production ортасында демо аккаунттар автоматты қосылмайды. Алғашқы әкімші үшін қорғалған орта конфигурациясына `Bootstrap:AdminEmail` және `Bootstrap:AdminPassword` орнатып, оператор ретінде **бөлек** `--seed` орындаңыз. Environment variable атаулары — `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword`. Production ортасында user-secrets автоматты жүктелмейді.

Екі мән де міндетті. Бапталған email арқылы әкімші рөлі беріледі; бар аккаунттың құпиясөзі ауыспайды. Бастапқы баптаудан кейін bootstrap құпиясөзін іске қосу ортасынан алып тастаңыз. Тіркелу беті тек `Student` рөлін береді; `Teacher` пен қосымша `Admin` рөлдерін әкімші интерфейсі басқарады. Соңғы әкімшінің рөлін алып тастауға сервер рұқсат бермейді.

## Файлдар және қолжетімділік

| Параметр | Әдепкі мән |
| --- | --- |
| `Storage:RootPath` | `App_Data/uploads` |
| `Storage:MaxFileSizeMb` | `20` |
| Қолдау | PDF |

Сақтау каталогы `wwwroot` сыртында болуы керек. Қосымшаның Windows process identity-іне сол каталогқа оқу/жазу құқығын беріңіз. Сервер файл атауын GUID арқылы жасайды; бастапқы атау тек метадерек ретінде сақталады. Кеңейтім, `%PDF-` сигнатурасы және нақты көлем тексеріліп, SHA-256 hash есептеледі. Ағынмен көшіру үлкен файлды түгел жедел жадқа жүктемейді.

Файл тек авторизацияны тексеретін controller арқылы беріледі. `Draft` және `Archived` — авторға және әкімшіге; `Published + Public` — барлық оқырманға; `Published + Authenticated` — тіркелген пайдаланушыға көрінеді. Жабық материал іздеу нәтижесіне де қосылмайды. PDF preview және Range сұраулары қосымша қаралым/жүктеу жасамайды; жүктеу саны арнайы POST батырмасымен есептеледі.

Сақтық көшірмеге **SQL базасын және upload каталогын бірге** қосыңыз: тек базаны көшіру PDF файлдарын қалпына келтірмейді. `App_Data`, build нәтижелері және жергілікті құпия конфигурация `.gitignore` ішінде. `DemoFiles/*.pdf` deploy құрамына кіреді, пайдаланушы upload файлдары бөлек басқарылуы керек.

## Тексеру

```powershell
dotnet restore
dotnet build --no-restore
dotnet test Tests/EduShelf.Tests.csproj --no-restore
```

SQL Server интеграциялық тесттері үшін жеке master қосылымын орнатыңыз:

```powershell
$env:EDUSHELF_TEST_CONNECTION = 'Server=.;Database=master;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True'
dotnet test Tests/EduShelf.Tests.csproj --no-restore
```

Бұл тесттер нақты SQL Server provider мен миграцияны қолданады. Әр тест fixture бірегей атауы бар уақытша база жасап, соңында жояды; login-ге `CREATE DATABASE` және оны жою құқығы қажет. Қосымшаның `EduShelf` базасын тест базасы ретінде көрсетудің қажеті жоқ. Айнымалы берілмесе SQL тесттері **өткізіліп жіберіледі**; бұл олардың тексеруден өткені болып саналмайды.

Тесттер серверлік авторизацияны, жабық материал іздеуін, файл валидациясын, orphan файлды тазалауды, тақырыптың пәнге тиесілігін, favorites бірегейлігін, рөлдер мен соңғы әкімшіні қорғауды қамтиды. Нақты іске қосу саны мен HTTP тексерулер [PHASE-1-STATUS.md](PHASE-1-STATUS.md) ішінде.

## Жоба құрылымы

```text
Controllers/        HTTP әрекеттері және endpoint авторизациясы
Data/               DbContext, миграциялар, explicit seed
Models/             Identity және кітапхана entity модельдері
ViewModels/         Формалар мен беттерге арналған модельдер
Services/           Қолжетімділік, каталог, файл, favorites, рөл ережелері
Views/              Қазақ тіліндегі Razor интерфейсі
wwwroot/css,js/     Бейімделетін дизайн, навигация, клиент валидациясы
DemoFiles/          Алты ашылатын оқу PDF-ы
scripts/            Idempotent SQL және демо PDF генераторы
Tests/              Файл/рұқсат unit тесттері және SQL интеграциялық тесттері
```

## Екінші кезең

`IResourceTextExtractor`, `IResourceAnalysisService`, `IRelatedResourcesService` келісімшарттары және `ResourceAnalysis` кестесі дайын. Қазіргі кезде олар AI провайдерін шақырмайды. Келесі кезеңде мәтін шығару, нақты талдау, кеңейтілген іздеу және аналитика қосылады. Файл ауысқанда алдыңғы талдау деректері тазартылады; талдау нәтижесі файл hash-ымен байланыстырылуға дайын.
