//.type AsmAddProc, %function

// asm_linux_arm64.S
// Syntaxe: GNU Assembler (AArch64)

.data
.align 4

Const0:                 .float  0.0
Const1:                 .float  1.0
Const1m:                .float  0.001
Const32:                .float  32.0

.align 4
ConstCopyBGRToBGR32:    .byte 0, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255
ConstCopyRGBToBGR32:    .byte 2, 1, 0, 255, 5, 4, 3, 255, 8, 7, 6, 255, 11, 10, 9, 255
ConstReverseRGBToBGR32: .byte 11, 10, 9, 255, 8, 7, 6, 255, 5, 4, 3, 255, 2, 1, 0, 255

DepthTransformImplConst:.float  0.001, 0.001, 0.001, 1.0

// C++ struktura AggregateItem má velikost 32 bajtů (8 * float)
// SumX(0), SumY(4), SumZ(8), Count(12), SumZ2(16), pad1(20), pad2(24), pad3(28)

.text
.align 4

// Exporty funkcí pro C++ / C#
.global AsmAddProc
.global TransformPoint4DImpl
.global Depth2XYZImpl
.global DepthTransformImpl
.global DepthTransform2Impl
.global XYZ2PlaneImpl
.global BackProjectImpl
.global BackProjectBGR32Impl
.global ClearAggregateImpl
.global AggregateObstaclesImpl
.global ExtractObstaclesImpl
.global ReverseInt16_8Impl
.global Copy_16Impl
.global CopyBGR24ToBGR32Impl
.global CopyRGB24ToBGR32Impl
.global ReverseRGB24ToBGR32Impl

// ============================================================================
// long long AsmAddProc(long long a, long long b)
// X0 - a, X1 - b -> Výsledek v X0
// ============================================================================
AsmAddProc:
    add x0, x0, x1
    ret

// ============================================================================
// void TransformPoint4DImpl(Point4D* dst, Point2D* transform, Point4D* src, int len)
// X0 - dst, X1 - transform, X2 - src, W3 - len (původně r9d)
// ============================================================================
TransformPoint4DImpl:
    cmp w3, #0
    ble .LTransform_Exit
    
    // Načtení transformační matice (4 řádky koeficientů)
    ld1 {v0.4s}, [x1], #16
    ld1 {v1.4s}, [x1], #16
    ld1 {v2.4s}, [x1], #16
    ld1 {v3.4s}, [x1]

    // Výpočet offsetu v bajtech: len * 16
    sxtw x3, w3
    lsl x3, x3, #4

.LTransform_Loop:
    sub x3, x3, #16
    
    // Načtení src[i]
    add x4, x2, x3
    ld1 {v4.4s}, [x4]

    // Násobení matice (ekvivalent pshufd + mulps + addps)
    // v4.s[0] je X, v4.s[1] je Y, v4.s[2] je Z, v4.s[3] je W
    fmul v5.4s, v0.4s, v4.s[0]
    fmla v5.4s, v1.4s, v4.s[1]
    fmla v5.4s, v2.4s, v4.s[2]
    fmla v5.4s, v3.4s, v4.s[3]

    // Uložení do dst[i]
    add x5, x0, x3
    st1 {v5.4s}, [x5]

    cbnz x3, .LTransform_Loop

.LTransform_Exit:
    ret

// ============================================================================
// int Depth2XYZImpl(Point4D* dst, short* dist, Point2D* transform, int len)
// X0 - dst, X1 - dist, X2 - transform, W3 - len
// ============================================================================
Depth2XYZImpl:
    cmp w3, #0
    ble .LDepth2XYZ_Zero
    
    sxtw x3, w3
    sub x3, x3, #1 // r9 = len - 1
    
    adrp x4, Const1
    ldr s1, [x4, :lo12:Const1]     // s1 = 1.0f
    adrp x4, Const1m
    ldr s6, [x4, :lo12:Const1m]    // s6 = 0.001f
    
    mov w4, #0                     // W4 = čítač úspěšných zápisů (EAX)

.LDepth2XYZ_Loop:
    // Načtení dist[r9] (short)
    lsl x5, x3, #1
    ldrsh w5, [x1, x5]             // w5 = sign-extended dist[i]
    
    cmp w5, #0
    b.eq .LDepth2XYZ_Next
    cmp w5, #-1
    b.eq .LDepth2XYZ_Next
    
    // Převod int na float a násobení 0.001
    scvtf s0, w5
    fmul s0, s0, s6                // s0 = dist * 0.001f
    
    // Načtení transform[r9] (dva floaty: x, y)
    lsl x5, x3, #3
    add x5, x2, x5
    ld1 {v5.2s}, [x5]              // v5.s[0]=x, v5.s[1]=y
    
    // OPRAVA SYNTAXE: Vytáhneme x a y do čistě skalárních registrů
    mov s16, v5.s[0]               // s16 = x
    mov s17, v5.s[1]               // s17 = y
    
    // Nyní již násobíme povoleným způsobem (skalár * skalár)
    fmul s2, s16, s0               // X = x * dist
    fmul s3, s17, s0               // Y = y * dist
    
    // Sestavení výsledného vektoru [x*dist, y*dist, dist, 1.0]
    // s2 = X, s3 = Y, s0 = Z, s1 = 1.0f
    mov v7.s[0], v2.s[0]           // X = zkopírujeme spodní prvek z v2 (kde je s2)
    mov v7.s[1], v3.s[0]           // Y = zkopírujeme spodní prvek z v3 (kde je s3)
    mov v7.s[2], v0.s[0]           // Z = dist
    mov v7.s[3], v1.s[0]           // W = 1.0f
    
    // Zápis do dst a posun pointeru
    st1 {v7.4s}, [x0], #16
    add w4, w4, #1

.LDepth2XYZ_Next:
    subs x3, x3, #1
    b.ge .LDepth2XYZ_Loop
    
    mov w0, w4                     // Návratová hodnota
    ret
.LDepth2XYZ_Zero:
    mov w0, #0
    ret

// ============================================================================
// int DepthTransformImpl(Point4D* dst, Point2D* transform, float* rotate, short* dist, int len)
// X0 - dst, X1 - transform, X2 - rotate, X3 - dist, W4 - len
// ============================================================================
DepthTransformImpl:
    cmp w4, #0
    ble .LDepthTrans_Zero
    
    sxtw x4, w4                    // X4 = len (r10d)
    
    // Načtení matice rotace
    ld1 {v0.4s}, [x2], #16
    ld1 {v1.4s}, [x2], #16
    ld1 {v2.4s}, [x2], #16
    ld1 {v3.4s}, [x2]
    
    // Načtení konstanty (0.001, 0.001, 0.001, 1.0)
    adrp x5, DepthTransformImplConst
    add x5, x5, :lo12:DepthTransformImplConst
    ld1 {v4.4s}, [x5]
    
    // Vynásobení matice konstantou
    fmul v0.4s, v0.4s, v4.4s
    fmul v1.4s, v1.4s, v4.4s
    fmul v2.4s, v2.4s, v4.4s
    // v3 se nenásobí (odpovídá x64 kódu)

    mov w5, #0                     // čítač (EAX)

.LDepthTrans_Loop:
    sub x4, x4, #1
    
    lsl x6, x4, #1
    ldrsh w6, [x3, x6]             // w6 = dist[i]
    
    cmp w6, #0
    ble .LDepthTrans_CheckLoop
    
    // Převod na float
    scvtf s5, w6                   // s5 = dist
    
    // Načtení transform[i] (x, y)
    lsl x7, x4, #3
    add x7, x1, x7
    ld1 {v7.2s}, [x7]              // v7.s[0]=x, v7.s[1]=y
    
    // Vytáhneme elementy do samostatných skalárů
    mov s21, v7.s[0]               // s21 = x
    mov s22, v7.s[1]               // s22 = y

    // Výpočet nenatočených XYZW: [x*dist, y*dist, dist, 1.0]
    fmul s16, s21, s5              // OPRAVENO: x * dist
    fmul s17, s22, s5              // OPRAVENO: y * dist
    fmov s18, s5                    // Z = dist
    adrp x7, Const1
    ldr s19, [x7, :lo12:Const1]    // W = 1.0f
    
    // Transformace maticí
    fmul v20.4s, v0.4s, v16.s[0]
    fmla v20.4s, v1.4s, v17.s[0]
    fmla v20.4s, v2.4s, v18.s[0]
    fmla v20.4s, v3.4s, v19.s[0]
    
    st1 {v20.4s}, [x0], #16
    add w5, w5, #1

.LDepthTrans_CheckLoop:
    cbnz x4, .LDepthTrans_Loop
    mov w0, w5
    ret
.LDepthTrans_Zero:
    mov w0, #0
    ret

// ============================================================================
// int DepthTransform2Impl(Point4D* dst, Point2D* transform, float* rotate, short* dist, int len)
// X0 - dst, X1 - transform, X2 - rotate, X3 - dist, W4 - len
// Vstup zpracováván inverzně, neplatné body uloženy jako [0,0,0,0]
// ============================================================================
DepthTransform2Impl:
    cmp w4, #0
    ble .LDepthTrans2_Zero
    
    sxtw x4, w4
    
    ld1 {v0.4s}, [x2], #16
    ld1 {v1.4s}, [x2], #16
    ld1 {v2.4s}, [x2], #16
    ld1 {v3.4s}, [x2]
    
    adrp x5, DepthTransformImplConst
    add x5, x5, :lo12:DepthTransformImplConst
    ld1 {v4.4s}, [x5]
    fmul v0.4s, v0.4s, v4.4s
    fmul v1.4s, v1.4s, v4.4s
    fmul v2.4s, v2.4s, v4.4s

.LDepthTrans2_Loop:
    sub x4, x4, #1
    
    lsl x6, x4, #1
    ldrsh w6, [x3, x6]
    
    cmp w6, #0
    ble .LDepthTrans2_Empty
    
    scvtf s5, w6
    lsl x7, x4, #3
    add x7, x1, x7
    ld1 {v7.2s}, [x7]
    
    // Vytáhneme elementy do samostatných skalárů
    mov s21, v7.s[0]               // s21 = x
    mov s22, v7.s[1]               // s22 = y
    
    // Výpočet nenatočených XYZW
    fmul s16, s21, s5              // OPRAVENO: x * dist
    fmul s17, s22, s5              // OPRAVENO: y * dist
    fmov s18, s5                    // Z = dist
    adrp x7, Const1
    ldr s19, [x7, :lo12:Const1]    // W = 1.0f
    
    fmul v20.4s, v0.4s, v16.s[0]
    fmla v20.4s, v1.4s, v17.s[0]
    fmla v20.4s, v2.4s, v18.s[0]
    fmla v20.4s, v3.4s, v19.s[0]
    b .LDepthTrans2_Store

.LDepthTrans2_Empty:
    movi v20.4s, #0

.LDepthTrans2_Store:
    st1 {v20.4s}, [x0], #16
    cbnz x4, .LDepthTrans2_Loop

.LDepthTrans2_Zero:
    ret

// ============================================================================
// void XYZ2PlaneImpl(float* param, Point4D* src, float MaxZ, int len)
// X0 - param, X1 - src, S2 - MaxZ, W3 - len
// ============================================================================
XYZ2PlaneImpl:
    // ARM64 AAPCS64: param=x0, src=x1, MaxZ=s0 (1. FP arg), len=w2 (2. integer arg).
    cmp w2, #0
    b.eq .LXYZ2Plane_Exit

    fmov s23, s0                   // zachovat MaxZ (s0/v0 se nize prepise nactenim sum)

    // Načtení stávajících sum z param
    ld1 {v0.4s}, [x0], #16
    ld1 {v1.4s}, [x0], #16
    ld1 {v2.4s}, [x0]
    sub x0, x0, #32                // Vrátit pointer na začátek

    sxtw x3, w2
    lsl x3, x3, #4                 // len * 16

.LXYZ2Plane_Loop:
    sub x3, x3, #16
    add x4, x1, x3
    ld1 {v3.4s}, [x4]              // v3 = (1, z, y, x)
    
    // Point4D v pameti = (x, y, z, a): v3.s[0]=x, s[1]=y, s[2]=z, s[3]=a(=1).
    mov v20.s[0], v3.s[0]          // X
    mov v21.s[0], v3.s[1]          // Y
    mov v22.s[0], v3.s[2]          // Z

    fabs s4, s22                   // |Z|
    fcmp s4, s23                   // Porovnání s MaxZ (zachovano v s23)
    bhi .LXYZ2Plane_Next

    // v0 += (x, y, z, 1): SumX, SumY, SumZ, Count1
    fadd v0.4s, v0.4s, v3.4s

    // v1 += (x*y, y*z, z*x, 1): SumXY, SumYZ, SumZX, Count2
    fmul s16, s20, s21             // x * y
    fmul s17, s21, s22             // y * z
    fmul s18, s22, s20             // z * x
    mov v4.s[0], v16.s[0]          // x * y
    mov v4.s[1], v17.s[0]          // y * z
    mov v4.s[2], v18.s[0]          // z * x
    mov v4.s[3], v3.s[3]           // 1 (a)
    fadd v1.4s, v1.4s, v4.4s
    
    // Cílové rozložení xmm2: (1, z*z, y*y, x*x)
    fmul v5.4s, v3.4s, v3.4s       // Umocnění všech prvků na druhou naráz
    fadd v2.4s, v2.4s, v5.4s

.LXYZ2Plane_Next:
    cbnz x3, .LXYZ2Plane_Loop
    
    // Zápis výsledků zpět
    st1 {v0.4s}, [x0], #16
    st1 {v1.4s}, [x0], #16
    st1 {v2.4s}, [x0]

.LXYZ2Plane_Exit:
    ret

// ============================================================================
// void BackProjectImpl(char* probability, BGR* img, char* backProjectTab, int len)
// X0 - probability, X1 - img, X2 - backProjectTab, W3 - len
// ============================================================================
BackProjectImpl:
    cmp w3, #0
    b.eq .LBackProject_Exit

.LBackProject_Loop:
    // Načtení 3 bajtů (B, G, R)
    ldrb w4, [x1]                  // B
    ldrb w5, [x1, #1]              // G
    ldrb w6, [x1, #2]              // R
    
    // Bitový posun a maskování horních 4 bitů (ekvivalent x64 postupu)
    and w4, w4, #0xF0
    and w5, w5, #0xF0
    and w6, w6, #0xF0
    
    // Složení 12bitového indexu: (R << 4) | G | (B >> 4)
    lsl w6, w6, #4
    orr w6, w6, w5
    lsr w4, w4, #4
    orr w6, w6, w4                 // w6 = výsledný index (rbx)
    
    // Vytažení hodnoty z tabulky a zápis do cíle
    ldrb w7, [x2, x6]
    strb w7, [x0], #1

    add x1, x1, #3                 // img += 3
    subs w3, w3, #1
    bne .LBackProject_Loop

.LBackProject_Exit:
    ret

// ============================================================================
// void BackProjectBGR32Impl(char* probability, BGR32* img, char* backProjectTab, int len)
// X0 - probability, X1 - img, X2 - backProjectTab, W3 - len
// ============================================================================
BackProjectBGR32Impl:
    cmp w3, #0
    b.eq .LBackProjectBGR32_Exit

.LBackProjectBGR32_Loop:
    ldr w4, [x1], #4               // Načtení celého intu (B, G, R, A) a posun img+=4
    
    // Extrakce jednotlivých kanálů a posun na horní 4 bity
    ubfx w5, w4, #16, #4           // R (vybere 4 bity od 16. bitu) -> index bit 8-11
    ubfx w6, w4, #8, #4            // G (vybere 4 bity od 8. bitu)  -> index bit 4-7
    ubfx w7, w4, #0, #4            // B (vybere 4 bity od 0. bitu)  -> index bit 0-3
    
    lsl w5, w5, #8
    lsl w6, w6, #4
    orr w5, w5, w6
    orr w5, w5, w7                 // w5 = výsledný index
    
    ldrb w8, [x2, w5, uxtw]
    strb w8, [x0], #1
    
    subs w3, w3, #1
    bne .LBackProjectBGR32_Loop

.LBackProjectBGR32_Exit:
    ret

// ============================================================================
// void ClearAggregateImpl(AggregateItem* ais, int32 *uais, int cnt)
// X0 - ais, X1 - uais, W2 - cnt
// ============================================================================
ClearAggregateImpl:
    cmp w2, #0
    ble .LClearAgg_Exit
    
    sxtw x2, w2
    movi v0.4s, #0                 // V0 = 0.0f (příprava pro nulování)

.LClearAgg_Loop:
    sub x2, x2, #1
    
    // Načtení indexu z uais
    lsl x3, x2, #2
    ldr w3, [x1, x3]               // w3 = index do ais
    
    // uais[i] je JIZ bajtovy offset (index * sizeof(AggregateItem)=32), viz native_funcs.cpp.
    // Adresa je tedy ais + uais[i], NIKOLIV ais + uais[i]*32.
    sxtw x3, w3
    add x4, x0, x3

    str s0, [x4, #12]              // ais[index].Count = 0.0f
    
    cbnz x2, .LClearAgg_Loop

.LClearAgg_Exit:
    ret

// ============================================================================
// int AggregateObstaclesImpl(Point4D* worldPoints, int worldPointsCount, double r, 
//                            int xOff, int yOff, AggregateItem* ais, int32* uais, 
//                            int width, int height, Point4D* v)
// X0-worldPoints, X1-worldPointsCount, D2-r, W3-xOff, W4-yOff, X5-ais, X6-uais, W7-width
// Na zásobníku: [sp] - height, [sp+8] - v (pointer)
// ============================================================================
AggregateObstaclesImpl:
    // ARM64 AAPCS64: worldPoints=x0, count=w1, r=d0, xOff=w2, yOff=w3,
    //   ais=x4, uais=x5, width=w6, height=w7, v (Point4D by value, HFA) = v1,v2,v3,v4.
    cmp w1, #0
    ble .LAggObst_Zero

    // Posbirat HFA 'v' (v1..v4) do v24 = (v.x, v.y, v.z, v.a) drive nez prepiseme v1-v4.
    mov v24.s[0], v1.s[0]          // v.x
    mov v24.s[1], v2.s[0]          // v.y
    mov v24.s[2], v3.s[0]          // v.z
    mov v24.s[3], v4.s[0]          // v.a

    // v0 = (r, r, r, r); r je float (C#/hpp: float r) predany v s0.
    dup v0.4s, v0.s[0]

    // v1 = (xOff, yOff, 0, 0)
    scvtf s1, w2                   // xOff
    scvtf s2, w3                   // yOff
    mov v1.s[1], v2.s[0]
    mov w11, #0
    mov v1.s[2], w11
    mov v1.s[3], w11

    mov v3.16b, v24.16b            // v3 = v = (v.x, v.y, v.z, v.a)

    sxtw x1, w1
    lsl x1, x1, #4                 // worldPointsCount * 16
    mov w10, #0                    // citac unikatnich indexu
.LAggObst_Loop:
    sub x1, x1, #16
    add x11, x0, x1
    ld1 {v7.4s}, [x11]             // v7 = worldPoints[i] = (1, z, y, x)
    
    fdiv v6.4s, v7.4s, v0.4s       // worldPoints / r
    
    // Zaokrouhlení na nejbližší celé číslo (roundps ekvivalent)
    frintn v6.4s, v6.4s
    fadd v6.4s, v6.4s, v1.4s       // v6 = (?, ?, y/r+yOff, x/r+xOff)
    
    // Kontrola mezí (0 <= x < width && 0 <= y < height)
    // fcvtzs (SIGNED) - zaporne souradnice musi zustat zaporne, aby se odfiltrovaly.
    fcvtzs w12, s6            // x_idx
    mov v25.s[0], v6.s[1]          // 1. Vytáhneme prvek z v6.s[1] do nulté pozice v25 (což je registr s25)
    fcvtzs w13, s25                // 2. Teď už provedeme převod ze čistého skaláru s25 do w13

    cmp w12, #0
    b.lt .LAggObst_Next            // x < 0 -> preskocit
    cmp w12, w6                    // x >= width -> preskocit
    bge .LAggObst_Next
    cmp w13, #0
    b.lt .LAggObst_Next            // y < 0 -> preskocit
    cmp w13, w7                    // y >= height -> preskocit
    bge .LAggObst_Next

    // Výpočet indexu do ais: ((y_idx * width) + x_idx) * 32
    madd w14, w13, w6, w12         // w6 = width
    lsl x14, x14, #5               // x14 = finální offset v bajtech do ais
    add x15, x4, x14               // x4 = ais
    
    // Výpočet nového z' = d + c*z + b*y + a*x (skalární součin v7 a v3)
    fmul v16.4s, v7.4s, v3.4s
    // Horizontální sečtení prvků vektoru v16
    faddp v16.4s, v16.4s, v16.4s
    faddp v16.4s, v16.4s, v16.4s   // s16 = z'
    
    // Point4D v pameti = (x, y, z, a); nahradime z (index 2) hodnotou z', aby SumZ += z'
    mov v7.s[2], v16.s[0]
    fmul s17, s16, s16             // s17 = z'^2
    
    // Načtení stávajícího Count
    ldr s18, [x15, #12]            // Count je na offsetu 12
    
    adrp x11, Const0
    ldr s19, [x11, :lo12:Const0]
    fcmp s18, s19
    beq .LAggObst_FirstTime
    
    // Již existující záznam - přičtení hodnot
    ldr s20, [x15, #16]            // 1. Načtení stávajícího SumZ2 z paměti do s20
    fadd s20, s20, s17             // 2. OPRAVENO: s20 = s20 + s17 (přičtení nové hodnoty)
    str s20, [x15, #16]            // 3. Uložení aktualizovaného SumZ2 zpět do paměti
    
    ld1 {v21.4s}, [x15]            // Načte SumX, SumY, SumZ, Count
    fadd v21.4s, v21.4s, v7.4s
    st1 {v21.4s}, [x15]
    b .LAggObst_Next

.LAggObst_FirstTime:
    // První zápis do této buňky
    st1 {v7.4s}, [x15]             // Zapíše SumX, SumY, SumZ, Count
    str s17, [x15, #16]            // Zapíše SumZ2
    
    // Uložení bajtoveho offsetu (index*32) do uais - shodne s C++ (uais[i]=idx*sizeof)
    // a s tim, co ocekava ClearAggregateImpl/ExtractObstaclesImpl.
    lsl x11, x10, #2
    str w14, [x5, x11]             // x5 = uais
    add w10, w10, #1

.LAggObst_Next:
    cbnz x1, .LAggObst_Loop
    mov w0, w10
    ret
.LAggObst_Zero:
    mov w0, #0
    ret

// ============================================================================
// int ExtractObstaclesImpl(AggregateItem* ais, int32* uais, int len, Point4D* ops, 
//                          float minCount, float minStd2)
// X0-ais, X1-uais, W2-len, X3-ops, S0-minCount, S1-minStd2
// ============================================================================
ExtractObstaclesImpl:
    cmp w2, #0
    ble .LExtract_Zero
    
    sxtw x2, w2
    mov w4, #0                     // Čítač výstupů (EAX)

.LExtract_Loop:
    sub x2, x2, #1
    
    lsl x5, x2, #2
    ldr w5, [x1, x5]               // w5 = index z uais
    
    sxtw x5, w5                    // uais[i] je jiz bajtovy offset (index*32)
    add x5, x0, x5                 // x5 = adresa AggregateItem = ais + uais[i]
    
    ld1 {v2.4s}, [x5]              // v2 = (SumX, SumY, SumZ, Count)
    ldr s3, [x5, #16]              // s3 = SumZ2
    mov s4, v2.s[3]                // s4 = Count
    
    fcmp s4, s0
    bls .LExtract_CheckLoop        // Pokud Count <= minCount, přeskočit
    
    // Výpočet průměrů: v2 / Count
    dup v6.4s, v4.s[0]
    fdiv v2.4s, v2.4s, v6.4s       // v2 = (X/C, Y/C, Z/C, 1.0f)
    
    // Výpočet rozptylu: (SumZ2 / Count) - (SumZ / Count)^2
    fdiv s3, s3, s4                // s3 = SumZ2 / Count
    mov s5, v2.s[2]                // s5 = Z / Count (střední hodnota Z)
    fmul s5, s5, s5                // s5 = (Z/C)^2
    fsub s3, s3, s5                 // s3 = Rozptyl
    
    fcmp s3, s1
    bls .LExtract_CheckLoop        // Pokud Rozptyl <= minStd2, přeskočit
    
    // Uložení platného objektu do ops
    st1 {v2.4s}, [x3], #16
    add w4, w4, #1

.LExtract_CheckLoop:
    cbnz x2, .LExtract_Loop
    mov w0, w4
    ret
.LExtract_Zero:
    mov w0, #0
    ret

// ============================================================================
// void ReverseInt16_8Impl(int16* dst, int16* src, int len)
// X0 - dst, X1 - src, W2 - len
// ============================================================================
ReverseInt16_8Impl:
    sxtw x2, w2
    lsl x2, x2, #1                 // len * 2 (velikost v bajtech)

.LRevInt16_Loop:
    ld1 {v0.8h}, [x1], #16         // Načtení 8 kusů int16 (16 bajtů)
    rev64 v0.8h, v0.8h             // Otočení prvků v rámci 64bitových polovin
    // Prohození horní a dolní poloviny (ekvivalent pshufd 04eh)
    ext v0.16b, v0.16b, v0.16b, #8 
    
    sub x2, x2, #16
    add x3, x0, x2
    st1 {v0.8h}, [x3]
    
    cbnz x2, .LRevInt16_Loop
    ret

// ============================================================================
// void Copy_16Impl(char* dst, char* src, int len)
// X0 - dst, X1 - src, W2 - len
// ============================================================================
Copy_16Impl:
    sxtw x2, w2
.LCopy16_Loop:
    sub x2, x2, #16
    add x3, x1, x2
    ld1 {v0.16b}, [x3]             // Načtení 16 bajtů
    add x4, x0, x2
    st1 {v0.16b}, [x4]             // Zápis 16 bajtů
    cbnz x2, .LCopy16_Loop
    ret

// ============================================================================
// void CopyBGR24ToBGR32Impl(BGR32* dst, BGR* src, int len)
// X0 - dst, X1 - src, W2 - len
// ============================================================================
CopyBGR24ToBGR32Impl:
    cmp w2, #0
    b.eq .LCopyBGR_Exit
    
    adrp x3, ConstCopyBGRToBGR32
    add x3, x3, :lo12:ConstCopyBGRToBGR32         // 1. Spočítáme kompletní 64-bitovou adresu do X3
    ld1 {v1.16b}, [x3]                             // 2. Teď už provedeme čisté načtení bez posunu

.LCopyBGR_Loop:
    ld1 {v0.16b}, [x1]             // 1. Načte 16 bajtů bez automatického posunu
    add x1, x1, #12                // 2. Ručně posune ukazatel X1 o 12 bajtů
    tbl v2.16b, {v0.16b}, v1.16b   // ARM NEON verze PSHUFB (zvektorovaný shuffle)
    st1 {v2.16b}, [x0], #16        // Uloží 16 bajtů (4 pixely BGR32)
    
    subs w2, w2, #4
    bgt .LCopyBGR_Loop

.LCopyBGR_Exit:
    ret

// ============================================================================
// void CopyRGB24ToBGR32Impl(BGR32* dst, RGB* src, int len)
// X0 - dst, X1 - src, W2 - len
// ============================================================================
CopyRGB24ToBGR32Impl:
    cmp w2, #0
    b.eq .LCopyRGB_Exit
    
    adrp x3, ConstCopyRGBToBGR32
    add x3, x3, :lo12:ConstCopyRGBToBGR32
    ld1 {v1.16b}, [x3]

.LCopyRGB_Loop:
    ld1 {v0.16b}, [x1]
    add x1, x1, #12
    tbl v2.16b, {v0.16b}, v1.16b
    st1 {v2.16b}, [x0], #16
    
    subs w2, w2, #4
    bgt .LCopyRGB_Loop

.LCopyRGB_Exit:
    ret

// ============================================================================
// void ReverseRGB24ToBGR32Impl(BGR32* dst, RGB* src, int len)
// X0 - dst, X1 - src, W2 - len
// ============================================================================
ReverseRGB24ToBGR32Impl:
    cmp w2, #0
    b.eq .LRevRGB_Exit
    
    sxtw x2, w2
    lsl x2, x2, #2                 // len * 4 (velikost dst v bajtech)
    
    adrp x3, ConstReverseRGBToBGR32
    add x3, x3, :lo12:ConstReverseRGBToBGR32
    ld1 {v1.16b}, [x3]

.LRevRGB_Loop:
    ld1 {v0.16b}, [x1]      //Načte 4 pixely ze src
    add x1, x1 , #12        // ale posunu se jen 4*3 bajty
    tbl v2.16b, {v0.16b}, v1.16b   // Přehází a otočí pořadí pixelů naráz
    
    sub x2, x2, #16
    add x4, x0, x2
    st1 {v2.16b}, [x4]             // Zapíše na inverzní pozici v dst
    
    cbnz x2, .LRevRGB_Loop

.LRevRGB_Exit:
    ret