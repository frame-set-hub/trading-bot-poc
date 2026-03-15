# RSI+Stoch Fibo Strategy — Logic Reference

ไฟล์: `pine/rsi_stoch_strategy.pine`

## Overview Flow

```mermaid
flowchart TD
    A[Stoch %K คำนวณ] --> B{Stoch เข้า OVB/OVS?}
    B -->|เข้า OVB k>80| C[จำ Price High ระหว่าง OVB]
    B -->|เข้า OVS k<20| D[จำ Price Low ระหว่าง OVS]
    B -->|อยู่กลาง| E[รอ]

    C -->|Stoch ออก OVB| F[บันทึก lastCompletedOvbHigh]
    D -->|Stoch ออก OVS| G[บันทึก lastCompletedOvsLow]

    F --> H[Support = lastCompletedOvsLow]
    G --> I[Resistance = lastCompletedOvbHigh]

    H --> J{ราคา Breakout?}
    I --> J

    J -->|close > Resistance| K[Breakout UP → Long Trend]
    J -->|close < Support| L[Breakout DOWN → Short Trend]
    J -->|ไม่เบรก| E

    K --> M[คำนวณ Fibo Targets]
    L --> M

    M --> N{Entry Conditions}
    N -->|Long + Stoch ไม่อยู่ OVB| O[🟢 LONG Entry]
    N -->|Short + Stoch ไม่อยู่ OVS| P[🔴 SHORT Entry]

    O --> Q[ตั้ง TP1/TP2 + SL]
    P --> Q

    Q --> R{Exit Conditions}
    R -->|ราคาถึง TP1| S[ปิด 50%]
    R -->|ราคาถึง TP2| T[ปิดอีก 50%]
    R -->|ราคาหลุด SL| U[ปิดทั้งหมด ❌]
    R -->|ราคาหลุด Trailing SL| V[ปิดทั้งหมด ❌]
    R -->|Trend Invalidated| W[ปิดทั้งหมด ❌]
    R -->|RSI เข้า OVB/OVS| X[ปิดทั้งหมด ❌ RSI Reset]
```

## Step-by-Step Detail

### Step 1: State Tracking

```mermaid
flowchart LR
    subgraph OVB Cycle
        A1[Stoch เข้า OVB] -->|k > 80| A2[จำ priceCycleHigh = high]
        A2 -->|ทุกแท่งใน OVB| A3[update max high]
        A3 -->|Stoch ออก OVB| A4[lastCompletedOvbHigh = priceCycleHigh]
    end

    subgraph OVS Cycle
        B1[Stoch เข้า OVS] -->|k < 20| B2[จำ priceCycleLow = low]
        B2 -->|ทุกแท่งใน OVS| B3[update min low]
        B3 -->|Stoch ออก OVS| B4[lastCompletedOvsLow = priceCycleLow]
    end
```

**ตัวอย่าง (XAUUSD 1D):**
- Stoch เข้า OVB → ราคา High = 3,450 → Stoch ออก OVB → `lastCompletedOvbHigh = 3,450`
- Stoch เข้า OVS → ราคา Low = 3,280 → Stoch ออก OVS → `lastCompletedOvsLow = 3,280`

### Step 2: Base Zone (กรอบปรับฐาน)

```mermaid
flowchart TD
    A[Stoch ออก OVS] --> B[Resistance = lastCompletedOvbHigh]
    C[Stoch ออก OVB] --> D[Support = lastCompletedOvsLow]

    E{RSI เข้า OVB หรือ OVS?} -->|ใช่| F[🔄 Reset ทุกอย่าง = ตัดภูเขา]
    F --> G[Resistance = na, Support = na]
```

| Event | ผลลัพธ์ |
|---|---|
| Stoch ออก OVS | Resistance = High ของ OVB cycle ที่ผ่านมา |
| Stoch ออก OVB | Support = Low ของ OVS cycle ที่ผ่านมา |
| RSI เข้า OVB/OVS | **Reset ทั้งหมด** (ตัดภูเขา) |

### Step 3: Trend Recognition

```mermaid
flowchart TD
    A{ราคา Breakout กรอบ?}

    A -->|close > Resistance| B[Breakout UP]
    A -->|close < Support| C[Breakout DOWN]

    B --> D{มี Higher Low ใน Stoch swings?}
    D -->|มี| E[✅ PERFECT Trend — โครงสร้างดี]
    D -->|ไม่มี| F[⚡ V-SHAPE Trend — เร็ว ไม่มีโครงสร้าง]

    C --> G{มี Lower High ใน Stoch swings?}
    G -->|มี| H[✅ PERFECT Trend]
    G -->|ไม่มี| I[⚡ V-SHAPE Trend]
```

**PERFECT vs V-SHAPE:**
- **PERFECT**: Stoch swing ก่อนหน้ามี Higher Low (ขาขึ้น) → โครงสร้างแข็ง → invalidation ยากกว่า
- **V-SHAPE**: ไม่มี structural confirmation → invalidation ง่ายกว่า (กลับตัว 30% ก่อนถึง Fibo 1.618 = ยกเลิก)

### Step 4: Fibonacci Targets

```mermaid
flowchart LR
    subgraph "Long (Breakout UP)"
        A1[Fibo Head = Support ที่ไม่ถูกเบรก] --> A2[Fibo End = Resistance ที่ถูกเบรก]
        A2 --> A3[TP1 = Head + range × 1.618]
        A2 --> A4[TP2 = Head + range × 2.618]
        A2 --> A5[MAX = Head + range × 4.236]
    end

    subgraph "Short (Breakout DOWN)"
        B1[Fibo Head = Resistance ที่ไม่ถูกเบรก] --> B2[Fibo End = Support ที่ถูกเบรก]
        B2 --> B3[TP1 = Head + range × 1.618]
        B2 --> B4[TP2 = Head + range × 2.618]
    end
```

**ตัวอย่าง Long:**
```
Support (Head)    = 3,280
Resistance (End)  = 3,450
Range             = 3,450 - 3,280 = 170

Fibo 1.618 (TP1)  = 3,280 + 170 × 1.618 = 3,555
Fibo 2.618 (TP2)  = 3,280 + 170 × 2.618 = 3,725
Fibo 4.236 (MAX)  = 3,280 + 170 × 4.236 = 4,000
```

### Step 5: Entry & Exit

```mermaid
flowchart TD
    subgraph "Entry Conditions"
        E1[Breakout UP] --> E2{Stoch อยู่ใน OVB?}
        E2 -->|ไม่ k≤80| E3[🟢 Long Entry]
        E2 -->|ใช่ k>80| E4[❌ ห้ามเข้า]

        E5[Breakout DOWN] --> E6{Stoch อยู่ใน OVS?}
        E6 -->|ไม่ k≥20| E7[🔴 Short Entry]
        E6 -->|ใช่ k<20| E8[❌ ห้ามเข้า]
    end

    subgraph "Position Management"
        E3 --> P1[SL = Low ของแท่ง Entry]
        E7 --> P2[SL = High ของแท่ง Entry]

        P1 --> P3[TP1 = Fibo 1.618 → ปิด 50%]
        P1 --> P4[TP2 = Fibo 2.618 → ปิดอีก 50%]
        P2 --> P3
        P2 --> P4
    end

    subgraph "Exit Triggers"
        X1[ราคาถึง TP1] --> X2[ปิด 50%]
        X3[ราคาถึง TP2] --> X4[ปิดอีก 50%]
        X5[ราคาหลุด SL] --> X6[ปิดทั้งหมด]
        X7[ราคาหลุด Trailing SL] --> X6
        X8[close < Fibo Head] --> X9[INVALIDATED → ปิดทั้งหมด]
        X10[RSI เข้า OVB/OVS] --> X11[RSI Reset → ปิดทั้งหมด]
    end
```

### Trailing Stop (Stoch Swing Structure)

```mermaid
flowchart TD
    A[Long Position Active] --> B{Stoch ทำ OVS cycle ใหม่?}
    B -->|ใช่| C[Trailing SL = Low ของ OVS cycle ล่าสุด]
    C --> D{OVS ใหม่มี Higher Low?}
    D -->|ใช่| E[SL ขยับขึ้น ✅]
    D -->|ไม่| F[SL คงที่]

    G[ราคาหลุด Trailing SL] --> H[ปิด Position ทั้งหมด ❌]
```

**ตัวอย่าง Trailing Stop (Long):**
```
OVS Cycle 1: Low = 3,280 → SL = 3,280
OVS Cycle 2: Low = 3,320 → SL ขยับขึ้นเป็น 3,320 (Higher Low)
OVS Cycle 3: Low = 3,350 → SL ขยับขึ้นเป็น 3,350
ราคาหลุด 3,350 → ปิด Position
```

### Invalidation Rules

```mermaid
flowchart TD
    A{Trend Active?}
    A -->|Long| B{close < Fibo Head?}
    B -->|ใช่| C[❌ INVALIDATED — ราคากลับมาต่ำกว่า Support เดิม]

    A -->|Short| D{close > Fibo Head?}
    D -->|ใช่| C

    A --> E{V-SHAPE Trend?}
    E -->|ใช่| F{ราคากลับตัว > 30% of range?}
    F -->|ใช่ + ยังไม่ถึง Fibo 1.618| G[❌ V-SHAPE INVALIDATED]
    F -->|ไม่| H[ยังถือต่อ]

    I{RSI เข้า OVB หรือ OVS?} -->|ใช่| J[❌ RSI Reset — ตัดภูเขา ยกเลิกทุกอย่าง]
```

## Files Architecture

```mermaid
flowchart LR
    subgraph "TradingView Chart"
        A["rsi_stoch_strategy.pine<br/>(overlay=true)<br/>Strategy + Trade Execution"]
        B["rsi_stoch_indicator.pine<br/>(overlay=false)<br/>RSI + Stoch + Divergence"]
    end

    A -->|"ใส่บน chart เดียวกัน"| C[Price Chart<br/>trade markers + Fibo lines]
    B -->|"pane ด้านล่าง"| D[Oscillator Pane<br/>RSI, %K/%D, OVB/OVS zones]
```

## Settings Reference

| Parameter | Default | คำอธิบาย |
|---|---|---|
| RSI Length | 14 | ความยาว RSI |
| %K / %D / Smoothing | 9 / 3 / 3 | Stochastic parameters |
| Stoch OVB/OVS | 80 / 20 | เกณฑ์ Overbought/Oversold |
| RSI OVB/OVS | 70 / 30 | เกณฑ์ RSI reset (ตัดภูเขา) |
| TP1 Fibo Level | 1.618 | Fibo level สำหรับ Take Profit 1 |
| TP2 Fibo Level | 2.618 | Fibo level สำหรับ Take Profit 2 |
| Use TP2 | true | แบ่ง 50/50 ที่ TP1 และ TP2 |
| Trade Direction | Both | Long / Short / Both |
| Initial Capital | 100,000 | ทุนเริ่มต้น backtest |
| Commission | 0.1% | ค่า commission ต่อ trade |

## Known Issues / TODO

- [ ] **Entry: แท่งกลับตัว** — ยังไม่ได้ใส่ logic candlestick reversal pattern (รอ confirm จาก user ว่าต้องการ pattern แบบไหน)
- [ ] **TP ติดลบ** — บางกรณี TP ถูก hit แต่ยังขาดทุน ต้องตรวจสอบว่า Fibo range กว้างพอหรือไม่
- [ ] **MTF Cluster** — ยังไม่ได้ใส่ใน strategy version (มีแค่ใน rsi_stoch_state.pine)
- [ ] **RSI Divergence** — ยังไม่ได้ใส่ใน strategy version (มีแค่ใน rsi_stoch_indicator.pine)
