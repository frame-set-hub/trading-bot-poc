# cTrader cAlgo Knowledge (macOS)

อ้างอิงข้อมูลที่ verify จาก cTrader.app เวอร์ชัน 5.9 บน macOS (เครื่อง framest, Darwin 24.5.0)

## 🔥 CRITICAL — `AccessRights.None` ห้ามใช้บน Apple Silicon (ARM64)

**Spotware ยืนยันบน forum [community.ctrader.com/forum/ctrader-support/40138](https://community.ctrader.com/forum/ctrader-support/40138/) ว่า:**
- cTrader บน macOS ARM64 (M1/M2/M3) **ยังไม่ support `AccessRights.None`**
- Algo ที่ใช้ `AccessRights.None` → child process ที่ต้อง run code โดน OS sandbox kill เงียบๆ → ไม่มี error log
- **อาการ**: indicator label + parameters + Lines tab แสดงครบ แต่ไม่มีเส้น plot และ Logs tab ว่างเปล่า — เพราะ Calculate() ไม่ได้ถูกเรียกเลย

**ทางแก้ที่ใช้ได้แน่นอน:** เปลี่ยนเป็น `AccessRights.FullAccess`
```csharp
[Indicator(AccessRights = AccessRights.FullAccess, IsOverlay = true)]
```

cTrader template default ที่ generate ออกมาใช้ `AccessRights.None` — ต้องแก้ทุกครั้งบน Mac

---

## ⚠️ CRITICAL — อย่ากด "Delete" ใน Algo panel

**cTrader's Delete จาก Algo panel จะลบทั้งโฟลเดอร์ source code ใน `~/cAlgo/Sources/<Type>/<Name>/` ทันที** ไม่ถามไม่ confirm — destructive operation ที่อันตรายมาก

แทนที่จะ Delete:
- เก็บ backup source ไว้นอก `~/cAlgo/` เสมอ (เช่น `~/Documents/ctrader-backup/`)
- Refresh code: Quit cTrader (⌘Q) → reopen → ลบ instance จาก chart → Add ใหม่
- ห้ามคลิก Delete ในเมนูคลิกขวาของ Algo panel

## Path & Layout

| สิ่ง | ตำแหน่ง |
|---|---|
| cTrader.app | `/Volumes/Frame WD_BLACK SN7100 1TB/study/trade/cTrader.app` |
| Source code (cBots, Indicators, Plugins) | `~/cAlgo/Sources/` |
| Compiled .algo (output) | `~/cAlgo/Sources/<Type>/<Name>.algo` (ระดับเดียวกับโฟลเดอร์โปรเจกต์) |
| Runtime data (Python venv ฯลฯ) | `~/cAlgo/Data/Indicators/<Name>/` |
| User config & journal | `~/cTrader/.config/Spotware/`, `~/cTrader/Journals/Spotware/Journal-YYYY-MM.txt` |
| dotnet (ใช้ build จาก CLI) | `/usr/local/share/dotnet/dotnet` (ไม่ได้อยู่ใน PATH) |

## Build จาก CLI

```bash
/usr/local/share/dotnet/dotnet build -c Release \
  "/path/to/<project>/<project>.csproj"
```

หลัง build ใหม่ cTrader จะ**ไม่** auto-reload — ต้อง:
1. ลบ instance จาก chart (right-click ที่ label → Remove)
2. (Optional) Quit cTrader แล้วเปิดใหม่
3. Drag .algo ใหม่เข้า Algo panel หรือ Add instance อีกครั้ง

## Inspect .NET DLL strings (debugging)

`strings` บน macOS อ่าน UTF-16 ไม่ได้ → .NET method body strings ตรวจไม่เจอด้วย `strings` ปกติ

ใช้ Python check แทน:
```python
data = open('path/to/file.dll', 'rb').read()
# Method body strings = UTF-16 LE
data.count('YOUR_STRING'.encode('utf-16-le'))
# Attribute strings = UTF-8
data.count('YOUR_STRING'.encode('utf-8'))
```

## C# Indicator Template (pure, ไม่ใช้ Python)

ตัวอย่าง csproj ขั้นต่ำ:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <IncludeSource>True</IncludeSource>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="cTrader.Automate" Version="*" />
  </ItemGroup>
</Project>
```

โครงคลาสมาตรฐาน: `[Indicator(...)]` → `[Parameter]` → `[Output]` → `Initialize()` → `Calculate(int index)`

## cAlgo.API namespace ที่จำได้

- `cAlgo.API` — `Indicator`, `Robot`, `MovingAverageType`, `LineStyle`, `AccessRights`, `IndicatorDataSeries`, `Bar`, `DataSeries`
- `cAlgo.API.Indicators` — `ExponentialMovingAverage`, `AverageTrueRange`, `BollingerBands`, ฯลฯ
- `cAlgo.API.Internals` — `IIndicatorsAccessor`, `Bars`, `Symbol`, `MarketSeries` (เน้น interfaces/services ไม่ใช่ enums)

> สำหรับ Python: `from cAlgo.API import MovingAverageType` ✅ ไม่ใช่ `cAlgo.API.Internals` ❌

## Python algos บน macOS — gotcha

cTrader ตอน build ใช้ Homebrew Python สร้าง venv ได้ก็จริง แต่ **runtime จริง** Python.Runtime.dll ต้องการ Python.org framework:

- Script: `cTrader.app/Contents/Resources/Scripts/install-python-runtime.sh`
- หา Python ที่: `/Library/Frameworks/Python.framework/Versions/Current/bin/python3` **เท่านั้น**
- ไม่มี → Python init fails **เงียบๆ** (Print delegate ยัง hook ไม่ทัน)
- **ทางแก้**: ติดตั้ง Python 3.13.x จาก python.org หรือ — แนะนำ — เขียน C# ล้วน

## Python+C# bridge template (cTrader generate ให้)

ไฟล์ที่ cTrader สร้างให้เมื่อเลือก AlgoLanguage=Python:
- `Engine.cs`, `EngineHelper.cs`, `IndicatorBridge.cs`, `BaseBridge.cs`
- `SafeExecuteMethodProxy.cs`, `PythonHooks.cs`, `PythonManifest.cs`, `EmbeddedResourceProvider.cs`
- `EmbeddedResources.manifest.json`
- `<name>_main.py`, `requirements.txt`, `http_requests.py`

Python class methods: `initialize`, `calculate(index)`, `on_destroy`, `on_timer`, `on_exception(e)`

`api` global ใน Python = instance ของ C# indicator class

## Logs / Debugging

- Chart's `Logs` tab = stdout จาก `Print()` (C#) หรือ `print()` (Python ที่ถูก hook)
- ⚠️ macOS `strings` tool ไม่อ่าน UTF-16 → check string ใน DLL ต้องใช้ Python
- File journal `~/cTrader/Journals/Spotware/Journal-YYYY-MM.txt` มีแค่ระดับ app (start/connect)
- Indicator log แต่ละ instance ดูได้ในแอปเท่านั้น (ไม่เขียนลงไฟล์)
