IFDEF RAX

;.586              ;Target processor.  Use instructions for Pentium class machines
;.MODEL FLAT, C    ;Use the flat memory model. Use C calling conventions
;.STACK            ;Define a stack segment of 1KB (Not required for this example)
.DATA             ;Create a near data segment.  Local variables are declared after
                  ;this directive (Not required for this example)

Const0			REAL4  0.0f
Const1			REAL4  1.0f
Const1m			REAL4  0.001f
Const32			REAL4  32.0f
Const1d			dword  1


ConstCopyBGRToBGR32	byte 0
				byte 1
				byte 2
				byte 0ffh
				byte 3
				byte 4
				byte 5
				byte 0ffh
				byte 6
				byte 7
				byte 8
				byte 0ffh
				byte 9
				byte 10
				byte 11
				byte 0ffh

ConstCopyRGBToBGR32	byte 2
				byte 1
				byte 0
				byte 0ffh
				byte 5
				byte 4
				byte 3
				byte 0ffh
				byte 8
				byte 7
				byte 6
				byte 0ffh
				byte 11
				byte 10
				byte 9
				byte 0ffh

ConstReverseRGBToBGR32	byte 11
				byte 10
				byte 9
				byte 0ffh
				byte 8
				byte 7
				byte 6
				byte 0ffh
				byte 5
				byte 4
				byte 3
				byte 0ffh
				byte 2
				byte 1
				byte 0
				byte 0ffh



DepthTransformImplConst 	dword  0.001f
				dword  0.001f
				dword  0.001f
				dword  1.0f



AggregateItem struct 
	SumX	REAL4 ?
	SumY	REAL4 ?
	SumZ	REAL4 ?
	Count	REAL4 ?
	SumZ2	REAL4 ?
	pad1	REAL4 ?
	pad2	REAL4 ?
	pad3	REAL4 ?
AggregateItem ends


.CODE             ;Indicates the start of a code segment.
align 16

; long long AsmAddProc(long long a, long long b)
; rcx - a
; rdx - b
AsmAddProc proc
    mov rax, rcx    ; Přesuneme první argument (a) ze složky RCX do RAX
    add rax, rdx    ; Přičteme k němu druhý argument (b) ze složky RDX
    ret             ; Návrat. Výsledek je v RAX, což C++ i C# automaticky přečtou.
AsmAddProc endp


;pole vektoru Point4D vynasobi matici transform a vysledek ulozi dst
;delka vstupniho a vystupniho pole je len
; r9d -len
; r8 - src
; rdx - transform
; rcx - dst

PUBLIC TransformPoint4DImpl

TransformPoint4DImpl PROC EXPORT

	movsxd	r9, r9d			;znamenkove rozsireni r9d do r9
	shl		r9, 4			;r9<<=4, 16 je pocet bajtu struktury Point4D tj. 4*float
	movups xmm0, [rdx]		;koeficienty pro x
    movups xmm1, 16[rdx]	;koeficienty pro y
    movups xmm2, 32[rdx]
    movups xmm3, 48[rdx]
    jmp TransformPoint4DImpl2

TransformPoint4DImpl1:
     movups xmm4, [r8+r9]			;xmm4=src[r9]

     pshufd xmm5, xmm4, 0			;xmm4[31:0] do ctyr spodnich floatu xmm5
     mulps xmm5, xmm0				;xmm5*=xmm0, 4 spodni floaty

	 pshufd  xmm6, xmm4, 055h		;vsechny 4 floaty xmm6 obsahuji xmm4[63:32]
     mulps xmm6, xmm1				;xmm6*=xmm1, 4 spodni floaty
     addps xmm5, xmm6				;xmm5+=xmm6, 4 spodni floaty

	 pshufd  xmm6, xmm4, 0aah		;vsechny 4 floaty xmm6 obsahuji xmm4[95:64]
     mulps xmm6, xmm2				;xmm6*=xmm2, 4 spodni floaty
     addps xmm5, xmm6				;xmm5+=xmm6, 4 spodni floaty

	 pshufd  xmm6, xmm4, 0ffh		;vsechny 4 floaty xmm6 obsahuji xmm4[127:96]
     mulps xmm6, xmm3				;xmm6*=xmm3, 4 spodni floaty
     addps xmm5, xmm6				;xmm5+=xmm6, 4 spodni floaty

     movups [rcx+r9], xmm5		;dst[r9]=xmm5

TransformPoint4DImpl2:
     sub	r9, 16
     jns TransformPoint4DImpl1
   ret 
TransformPoint4DImpl ENDP 


;z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste dopprava, y - roste dolu a z od kamery)
;transform je pole vektoru xy, plati xyz=[x*dist, y*dist, dist], pole transform a src obsahuje len prvku,
;nektere hodnoty v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
;funkce vraci pocet zapsanych zaznamu do dst
; r9d -len
; r8 - transform
; rdx - dist
; rcx - dst


Depth2XYZImpl PROC EXPORT
	movsxd		r9, r9d			;r9=len, znamenkove rozsireni r9d do r9
    sub			r9, 1

	movss		xmm1, dword ptr [Const1]
    pshufd		xmm0, xmm1, 000h		;vsechny 4 floaty xmm0 obsahuji xmm1[31:0]
	movss		xmm6, dword ptr [Const1m]

	mov			eax, 0
Depth2XYZ_1:
	movsx		ebx, word ptr [rdx+2*r9]		;edx=dist[r9]
	cmp			ebx, 0
	je			Depth2XYZ_2						;vzdalenost 0 preskocit
	cmp			ebx, -1
	je			Depth2XYZ_2						;vzdalenost -1 preskocit
	CVTSI2SS 	xmm0, ebx						;xmm0(31:0)=dist[r9] 
	mulss		xmm0, xmm6						;xmm0=0.001*dist[r9] 
    pshufd		xmm3, xmm0, 040h				;xmm3 3 nejnizsi floaty = dist[ebx], nejvyssi =1

	movq		xmm5, qword ptr [r8+8*r9]		;xmm4 =transfer[r9] (y, x)
    shufps		xmm5, xmm1, 004h				;xmm5=(1, 1, y, x)

	mulps		xmm5, xmm3						;xmm5*=xmm3, 4 spodni floaty, nejnizsi by mel byt 1

	movups		[rcx], xmm5						;dst[rcx]=xmm5
	add			rcx, 16
	inc			eax
Depth2XYZ_2:

	dec	r9
	jge Depth2XYZ_1

	ret 
Depth2XYZImpl ENDP 



; int DepthTransformImpl(Point4D* dst, Point2D* transform, float* rotate, short* dist, int len);
;z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery)
;nasledne bod pootoci v prostoru
;transform je pole vektoru xy, plati xyz=[x*dist, y*dist, dist], pole transform a src obsahuje len prvku,
;nektere hodnoty v dist reprezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
;funkce vraci pocet zapsanych zaznamu do dst

;pole vektoru Point4D vynasobi matici transform a vysledek ulozi dst
;delka vstupniho a vystupniho pole je len


; [rsp+40] - len
; r9	- dist
; r8	- rotate
; rdx	- transform
; rcx	- dst

DepthTransformImpl PROC EXPORT
	mov			r10d, dword ptr [rsp+40] 
	push		rbx
	movups		xmm0, [r8]		;r8=rotate
    movups		xmm1, 16[r8]
    movups		xmm2, 32[r8]
    movups		xmm3, 48[r8]				; r8 - dale uz rotate neni potreba
	movups		xmm4, dword ptr [DepthTransformImplConst]	; xmm4=(1, 0.001, 0.001, 0.001), prevod mm na m
    mulps		xmm0, xmm4					;xmm0*=(1, 0.001, 0.001, 0.001)
    mulps		xmm1, xmm4					;xmm1*=(1, 0.001, 0.001, 0.001)
    mulps		xmm2, xmm4					;xmm2*=(1, 0.001, 0.001, 0.001)

	movss		xmm4, dword ptr [Const1]
	pshufd		xmm5, xmm4, 000h			; vsechny 4 floaty xmm5 obsahuji 1

	mov			eax, 0						; pocitadlo zapisu
	jmp			DepthTransformImpl_2

DepthTransformImpl_1:
	movsx		ebx, word ptr [r9+2*r10]	;edx=dist[r10]
	cmp			ebx, 0
	jle			DepthTransformImpl_2		;vzdalenost <=0 preskocit

	CVTSI2SS 	xmm5, ebx					;xmm5=(1, 1, 1, (float)dist[edx])

	pshufd		xmm6, xmm5, 0c0h			;xmm6=(1, dist[edx], dist[edx], dist[edx])
	movq		xmm7, qword ptr [rdx+8*r10]	;xmm7 =transfer[r10] (y, x)
    shufps		xmm7, xmm5, 0f4h			;xmm7=(1, 1, y, x)

	mulps		xmm7, xmm6					;xmm7*=xmm6, nejvyssi float by mel byt 1

	pshufd		xmm6, xmm7, 000h			;vsechny 4 floaty xmm6 obsahuji xmm7[31:0]
    mulps		xmm6, xmm0					;xmm6*=xmm0, suma jednotlivych slozek vektoru
	
	pshufd		xmm4, xmm7, 055h			;vsechny 4 floaty xmm4 obsahuji xmm7[63:32]
    mulps		xmm4, xmm1					;xmm4*=xmm1
    addps		xmm6, xmm4					;xmm6+=xmm4
     
	pshufd		xmm4, xmm7, 0aah			;vsechny 4 floaty xmm4 obsahuji xmm7[95:64]
    mulps		xmm4, xmm2					;xmm4*=xmm2
    addps		xmm6, xmm4					;xmm6+=xmm4

	pshufd		xmm4, xmm7, 0ffh			;vsechny 4 floaty xmm4 obsahuji xmm7[127:96]
    mulps		xmm4, xmm3					;xmm4*=xmm3
    addps		xmm6, xmm4					;xmm6+=xmm4

	movups		[rcx], xmm6					;dst[rcx]=xmm6
	add			rcx, 16
	inc			eax
DepthTransformImpl_2:
	sub			r10d, 1
	jge			DepthTransformImpl_1

	pop			rbx
	ret 
DepthTransformImpl ENDP 


;int DepthTransform2Impl(Point4D* dst, Point2D* transform, float* rotate, short* dist, int len);
;z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery)
;nasledne bod pootoci v prostoru pomoci rotate
;transform je pole vektoru xy, plati dst[i]=[transform[i].x*dist[i], transform[i].y*dist[i], dist[i]]*rotate, pole transform, dst a dist obsahuje len prvku,
;nektere hodnoty v dist reprezentuji nezmerenou hodnotu, tyto body se do vystupu dst ulozi jako [0, 0, 0, 0]
;data se do dst ukladaji v opacnem poradi oproti dist


; [rsp+40] - len
; r9	- dist
; r8	- rotate
; rdx	- transform
; rcx	- dst

DepthTransform2Impl PROC EXPORT
	mov			r10d, dword ptr [rsp+40] 
	push		rbx
	movups		xmm0, [r8]		;r8=rotate
    movups		xmm1, 16[r8]
    movups		xmm2, 32[r8]
    movups		xmm3, 48[r8]				; r8 - dale uz rotate neni potreba
	movups		xmm4, dword ptr [DepthTransformImplConst]	; xmm4=(1, 0.001, 0.001, 0.001), prevod mm na m
    mulps		xmm0, xmm4					;xmm0*=(1, 0.001, 0.001, 0.001)
    mulps		xmm1, xmm4					;xmm1*=(1, 0.001, 0.001, 0.001)
    mulps		xmm2, xmm4					;xmm2*=(1, 0.001, 0.001, 0.001)

	movss		xmm4, dword ptr [Const1]
	pshufd		xmm5, xmm4, 000h			; vsechny 4 floaty xmm5 obsahuji 1

	jmp			DepthTransform2Impl_4

DepthTransform2Impl_1:
	movsx		ebx, word ptr [r9+2*r10]	;edx=dist[r10]
	cmp			ebx, 0
	jle			DepthTransform2Impl_2		;vzdalenost <=0 preskocit

	CVTSI2SS 	xmm5, ebx					;xmm5=(1, 1, 1, (float)dist[edx])

	pshufd		xmm6, xmm5, 0c0h			;xmm6=(1, dist[edx], dist[edx], dist[edx])
	movq		xmm7, qword ptr [rdx+8*r10]	;xmm7 =transfer[r10] (y, x)
    shufps		xmm7, xmm5, 0f4h			;xmm7=(1, 1, y, x)

	mulps		xmm7, xmm6					;xmm7*=xmm6, nejvyssi float by mel byt 1

	pshufd		xmm6, xmm7, 000h			;vsechny 4 floaty xmm6 obsahuji xmm7[31:0]
    mulps		xmm6, xmm0					;xmm6*=xmm0, suma jednotlivych slozek vektoru
	
	pshufd		xmm4, xmm7, 055h			;vsechny 4 floaty xmm4 obsahuji xmm7[63:32]
    mulps		xmm4, xmm1					;xmm4*=xmm1
    addps		xmm6, xmm4					;xmm6+=xmm4
     
	pshufd		xmm4, xmm7, 0aah			;vsechny 4 floaty xmm4 obsahuji xmm7[95:64]
    mulps		xmm4, xmm2					;xmm4*=xmm2
    addps		xmm6, xmm4					;xmm6+=xmm4

	pshufd		xmm4, xmm7, 0ffh			;vsechny 4 floaty xmm4 obsahuji xmm7[127:96]
    mulps		xmm4, xmm3					;xmm4*=xmm3
    addps		xmm6, xmm4					;xmm6+=xmm4

	jmp			DepthTransform2Impl_3
DepthTransform2Impl_2:
	movss		xmm4, dword ptr [Const0]
	pshufd		xmm6, xmm4, 000h			; vsechny 4 floaty xmm6 obsahuji 0

DepthTransform2Impl_3:
	movups		[rcx], xmm6					;dst[rcx]=xmm6
	add			rcx, 16

DepthTransform2Impl_4:
	sub			r10d, 1
	jge			DepthTransform2Impl_1

	pop			rbx
	ret 
DepthTransform2Impl ENDP 










;Prolozi rovinou mnozinu bodu jejich z je meni jak MaxZ.
;Lepe receno spocte parametry pro vypocet prolozeni.
;Bude davat spravny vysled i kdyz pole bude obsahovat prazne body tj. (0, 0, 0, 0)
; r9d - len
; xmm2 - MaxZ
; rdx - Point4D* src 
; rcx - float* param - sum(1), sum(z), sum(y), sum(x), sum(1), sum(z*x), sum(y*z), sum(x*y), sum(1), sum(z*z), sum(y*y), sum(x*x)

;xmm0 = suma (1, z, y, x)
;xmm1 = suma (1, z*x, y*z, x*y)
;xmm2 = suma (1, z*z, y*y, x*x)

XYZ2PlaneImpl PROC EXPORT
	cmp		r9d, 0		;pokud len==0 return
	je		XYZ2Plane3

	movss	xmm5, xmm2	;xmm5=MaxZ

	movups	xmm0, [rcx]
	movups	xmm1, [rcx+16]
	movups	xmm2, [rcx+32]

	movsxd	r9, r9d			;znamenkove rozsireni len do r9
	shl		r9, 4			;r9<<=4, 16 je pocet bajtu struktury Point4D tj. 4*float

XYZ2Plane1:
	movaps  xmm3, [rdx+r9-16]		;xmm3=src[r9]
	pextrd	eax, xmm3, 2	;xmm4=src[r9].z
	movd	xmm4, eax	

	xorps	xmm6, xmm6		;xmm6=0
	subss	xmm6, xmm4		;xmm6=xmm6-xmm4
	maxss	xmm4, xmm6		;xmm4=max(xmm4, xmm6)=max(xmm4, 0-xmm4)=abs(xmm4)

	ucomiss	xmm4, xmm5
	jnc	XYZ2Plane2			;skoc kdyz xmm4>xmm5
	addps 	xmm0, xmm3		;xmm0+=xmm3

	pshufd	xmm4, xmm3, 0c9h	;xmm4 = (1, x, z, y)
	mulps 	xmm4, xmm3		;xmm4*=xmm3, xmm4=(1, z*x, y*z, x*y)
	addps 	xmm1, xmm4		;xmm1+=xmm4
	mulps 	xmm3, xmm3		;xmm3*=xmm3, xmm4=(1, z*z, y*y, x*x)
	addps 	xmm2, xmm3		;xmm2+=xmm3
XYZ2Plane2:
	sub		r9, 16
	jne		XYZ2Plane1

	movups	[rcx], xmm0
	movups	[rcx+16], xmm1
	movups	[rcx+32], xmm2

XYZ2Plane3:
	ret 
XYZ2PlaneImpl ENDP 

; void BackProjectImpl(char* probability, BGR* img, char* backProjectTab, int len);
; Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
; r9d - len
; r8 - backProjectTab
; rdx - img
; rcx - probability

BackProjectImpl PROC EXPORT

BackProjectImpl1:
	mov		eax, dword ptr[rdx] ;eax=img[rdx]
	and		eax, 0f0f0f0h		;eax&=0xf0f0f0
	shr		eax, 4				;eax>>=4
	and		rbx, 0				;rbx=0
	or		bl, al				;bx=((img[esi]&0xf0)>>4)
	shr		eax, 4				;eax>>=4
	or		bl, al				;bx|=((img[esi]&0xf000)>>8)
	shr		eax, 4				;eax>>=4
	or		bh, ah				;bx|=((img[esi]&0xf00000)>>12)
	mov		al, byte ptr[R8+rbx] ;al=backProjectTab[rbx]
	mov		byte ptr[rcx], al  ;dst[edi]=al
	add		rdx, 3
	inc		rcx
	dec		r9d
	jne		BackProjectImpl1

	ret 
BackProjectImpl ENDP 

; void BackProjectBGR32Impl(char* probability, BGR32* img, char* backProjectTab, int len);
; Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
; r9d - len
; r8 - backProjectTab
; rdx - img
; rcx - probability

BackProjectBGR32Impl PROC EXPORT

BackProjectBGR32Impl1:
	and		rbx, 0				;rbx=0
	mov		eax, dword ptr[rdx] ;eax=img[rdx]
	and		eax, 0f0f0f0h		;eax&=0xf0f0f0
	or		bl, ah				;bx|=((img[esi]&0xf000)>>8)
	shr		eax, 4				;eax>>=4
	or		bh, al				;bx=((img[esi]&0xf0)>>4)
	shr		eax, 8				;eax>>=8
	or		bl, ah				;bx|=((img[esi]&0xf00000)>>12)
	mov		al, byte ptr[R8+rbx] ;al=backProjectTab[rbx]
	mov		byte ptr[rcx], al  ;dst[edi]=al
	add		rdx, 4
	inc		rcx
	dec		r9d
	jne		BackProjectBGR32Impl1

	ret 
BackProjectBGR32Impl ENDP 

;void ClearAggregateImpl(AggregateItem* ais, int32 *uais, int cnt);
; Inizializuje pouzite agregacni itemy nastavenim Count na 0
; a je pole ukazatelu na agregacni itemy
; delka vstupniho a vystupniho pole je len
; r8 - cnt
; rdx - uais
; rcx - ais
;

ClearAggregateImpl PROC EXPORT
 	mov		rsi, rdx					;rsi=uais
 	mov		rdi, rcx					;rdi=ais
	mov		rdx, r8						;rdx=cnt
	xor		rcx, rcx					;rcx=0;
	mov		eax, dword ptr [Const0]		;eax=(float)0
	jmp     ClearAggregateImpl_2

ClearAggregateImpl_1:
    mov   ecx, dword ptr[rsi+4*rdx]		;rcx=[rdx]
    mov   (AggregateItem ptr[rdi+rcx]).Count, eax	;a[rdx]->Count=0
ClearAggregateImpl_2:

    sub   rdx, 1
    jge   ClearAggregateImpl_1
    ret 
ClearAggregateImpl ENDP 



; int AggregateObstaclesImpl(Point4D* wordPoints, int wordPointsCount, double r, int xOff, int yOff, AggregateItem* ais, int32* uais, int width, int height, Point4D v)

; [rsp+80] - v
; [rsp+72] - height
; [rsp+64] - width
; [rsp+56] - uais
; [rsp+48] - ais

; [rsp+40] - yOff
; r9 - xOff
; xmm2 - r
; rdx - wordPointsCount
; rcx - wordPoints

AggregateObstaclesImpl PROC EXPORT
 	mov			rax, rsp

	push		rdi
	push		rsi
	push		rbx
	push		rbp

	sub			rsp, 16
	movdqu		[rsp], xmm6
	sub			rsp, 16
	movdqu		[rsp], xmm7

	mov			rbp, rax

	xor			rax, rax		;rax=0

	mov			rsi, rcx		;rsi=wordPoints
	mov			rbx, rdx		;rbx=wordPointsCount
	shl			rbx, 4			;rbx*=16, 16 je velikost Point4D
	movss		xmm0, xmm2		;xmm0=r
    shufps		xmm0, xmm0, 0	;vsechny 4 floaty xmm0 obsahuji r
	movd		xmm1, r9		;xmm1=xOff
	movd		xmm2, dword ptr [rbp+40]	;xmm2=yOff
	insertps	xmm1, xmm2, 010h			;xmm1=(0, 0, yOff, xOff)
	CVTDQ2PS	xmm1, xmm1					;prevede xmm1 na floaty
	 
	mov			rdi, [rbp+48]	;edi=ais
	mov			rcx, [rbp+56]	;ecx=uais
	movd		xmm2, dword ptr [rbp+64]	;xmm2=width

	movd		xmm4, [Const1d]				;xmm4=(0, 0, 0, 1)
	insertps	xmm4, xmm2, 010h			;xmm4=(0, 0, width, 1)
	CVTDQ2PS	xmm4, xmm4					;prevede xmm4 na floaty
	movss		xmm3, [Const32]
    shufps		xmm3, xmm3, 0				;vsechny 4 floaty xmm3 obsahuji 32
	mulps		xmm4, xmm3					;xmm4=(0, 0, 32*width, 32)

	movd		xmm3, dword ptr [rbp+72]	;xmm3=height
	insertps	xmm2, xmm3, 010h			;xmm2=(0, 0, height, width)
	CVTDQ2PS	xmm2, xmm2					;prevede xmm2 na floaty
	mov			rdx, [rbp+80]				;rdx=*v, nevim proc se v predava jako pointer kdyz je to deklarovano jako Point4D, ale je to tak
	movups		xmm3, [rdx]					;xmm3=v=(d, c, b, a)


	jmp		AggregateObstaclesImpl2

AggregateObstaclesImpl1:
	movups	xmm7, [rsi+rbx]			;xmm7=(1, z, y, x)=wordPoints[ebx]
	movaps	xmm6, xmm7
	divps	xmm6, xmm0				;xmm6=wordPoints[ebx]/r
	roundps	xmm6, xmm6,0			;zaokrouhleni
	addps	xmm6, xmm1				;xmm6=(?, ?, (int32)(y/r)+yOff, (int32)(x/r)+xOff)  
	movaps	xmm5, xmm6				;xmm5=(?, ?, (int32)(y/r)+yOff, (int32)(x/r)+xOff)  
	pshufd  xmm6, xmm6, 044h		;xmm6=((int32)(y/r)+yOff, (int32)(x/r)+xOff, (int32)(y/r)+yOff, (int32)(x/r)+xOff)  

	CMPLTPS xmm6, xmm2				;((int32)(y/r)+yOff, (int32)(x/r)+xOff, (int32)(y/r)+yOff, (int32)(x/r)+xOff)<(0, 0, height, width)
	MOVMSKPS edx, xmm6				;extrakce znamenek, pokud predchozi porovnani plati je znamenko rovno 1
	cmp		edx, 3					;hodnoty nesmi byt mensi nule a musi byt menis hornim mezim
	jne		AggregateObstaclesImpl2 ;takze pokud nejsem v mezich tak skok

	mulps	xmm5, xmm4				;xmm5=(0, 0, ((int32)(y/r)+yOff)*32*width, ((int32)(x/r)+xOff)*32)
	haddps	xmm5, xmm5				;xmm5=(0, ((int32)(y/r)+yOff)*32*width+((int32)(x/r)+xOff)*32, 0, ((int32)(y/r)+yOff)*32*width+((int32)(x/r)+xOff)*32) tj. index do ais
	CVTSS2SI rdx, xmm5				;edx=index do ais
	movaps	xmm6, xmm7				;xmm6=(1, z, y, x)
	mulps	xmm6, xmm3				;xmm6=(d, c*z, b*y, a*x)
	haddps	xmm6, xmm6				;xmm6=(0, 0, d+c*z, b*y+a*x)
	haddps	xmm6, xmm6				;xmm6=(0, 0, 0, d+c*z+b*y+a*x) tj nove z'
	insertps	xmm7, xmm6, 020h		;xmm7=(1, z', y, x)
	mulss	xmm6, xmm6				;xmm6=z'^2

	movss	xmm5, (AggregateItem ptr[rdi+rdx]).Count			;xmm5=ais[rdx]
	comiss	xmm5, [Const0]			;xmm5==0
	je		AggregateObstaclesImpl3 ;ano 

	addss	xmm6, (AggregateItem ptr[rdi+rdx]).SumZ2	;xmm6+=ais[edx].SumZ2
	movss	(AggregateItem ptr[rdi+rdx]).SumZ2, xmm6	;ais[edx].SumZ2=xmm6
	movups	xmm6, dword ptr[rdi+rdx]			;xmm6=ais[edx]
	addps	xmm7, xmm6							;xmm7+=ais[edx], pouziva se scitani pres register XMM^, aby mohl byt nacten z lokace ktera neni zarovnana na 16 nasobek
	movups	dword ptr[rdi+rdx], xmm7			;ais[edx]=xmm7

	jmp		AggregateObstaclesImpl2


AggregateObstaclesImpl3:
	movups	dword ptr[rdi+rdx], xmm7			;ais[edx]=xmm7=(1, z', y, x)
	movss	(AggregateItem ptr[rdi+rdx]).SumZ2, xmm6			;ais[edx].SumZ2=xmm6=z'^2
	mov		dword ptr[rcx+4*rax], edx			;*uais=edx tj index do ais

	inc		rax

AggregateObstaclesImpl2:
	sub		rbx, 16
	jns		AggregateObstaclesImpl1

	movdqu		xmm7, [rsp]
	add			rsp, 16
	movdqu		xmm6, [rsp]
	add			rsp, 16

	pop			rbp
	pop			rbx
	pop			rsi
	pop			rdi

	ret 
AggregateObstaclesImpl ENDP 



; int ExtractObstacles(Aggregateitem* ais, int32* uais, int len, Point4D* ops, float minCount, float minStd2)


; [rsp+48] - minStd2
; [rsp+40] - minCount
; r9 - ops
; r8 - len
; rdx - uais
; rcx - ais


ExtractObstaclesImpl PROC EXPORT
 	mov			rax, rsp

	push		rdi
	push		rsi
	push		rbx
	push		rbp

	sub			rsp, 16
	movdqu		[rsp], xmm6
	sub			rsp, 16
	movdqu		[rsp], xmm7

	mov			rbp, rax

	xor		eax, eax		;eax=0

	mov		rsi, rdx		;rsi=uais
	mov		rbx, r8			;rbx=len
	mov		rdi, r9			;rdi=ops
	movss	xmm0, dword ptr [rbp+40]	;xmm0=minCount
	movss	xmm1, dword ptr [rbp+48]	;xmm1=minStd2
	movss	xmm2, dword ptr [Const1]	;xmm2=(0, 0, 0, 1)
	mov		rbp, rcx		;ebp=ais

	xor		rcx, rcx

	jmp		ExtractObstaclesImpl2

ExtractObstaclesImpl1:
	mov		ecx, dword ptr[rsi+4*rbx] ;rcx=uias[rbx]
;varianta AggregateItem SumX, SumY, SumZ, Count, SumZ2
	movups	xmm3, [rbp+rcx]			;xmm3=(Count, SumZ, SumY, SumX)
	pshufd  xmm4, xmm3, 0ffh		;vsechny 4 floaty xmm4 obsahuji ais[uias[ebx]]->Count

	comiss  xmm4, xmm0
	jbe		ExtractObstaclesImpl2	;jmp pokud je ais[uias[rbx]]->Count<=minCount
	divps	xmm3, xmm4			;xmm3=(1, SumZ/Count, SumY/Count, SumX/Count)

	movss	xmm5, (AggregateItem ptr[rbp+rcx]).SumZ2	;xmm5=ais[uias[rbx]]->SumZ2
	divss	xmm5, xmm4			;xmm5=SumZ2/Count

	pshufd  xmm6, xmm3, 2		;xmm6=SumZ/Count
	mulss	xmm6, xmm6
	subss	xmm5, xmm6			;xmm5=SumZ2/ais[uias[rbx]]->Count-(SumZ/ais[uias[rbx]]->Count)^2

	comiss  xmm5, xmm1			;
	jbe		ExtractObstaclesImpl2	;jmp pokud je xmm5<xmm1 tj. std(z)^2<=minStd2

	movups	[rdi], xmm3			;ops[rax]->(w, z, y, x)=xmm3=(1, SumZ/Count, SumY/Count, SumX/Count)

	inc		rax
	add		rdi, 16

ExtractObstaclesImpl2:
	dec		rbx
	jns		ExtractObstaclesImpl1

	movdqu		xmm7, [rsp]
	add			rsp, 16
	movdqu		xmm6, [rsp]
	add			rsp, 16

	pop			rbp
	pop			rbx
	pop			rsi
	pop			rdi

	ret 
ExtractObstaclesImpl ENDP 



;void ReverseInt16_8(int16* dst, int16* src, int len);
;reverzuje pole Int16, ze zdroje src kopiruje do dst
;delka vstupniho a vystupniho pole je len a musi byt v nasobcich 8
; r8 -len
; rdx - src
; rcx - dst 

ReverseInt16_8Impl PROC 
	shl			r8, 1			;r8=len*2
	jmp			ReverseInt16_8Impl2
ReverseInt16_8Impl1:
	movups		xmm0, dword ptr [rdx]			;xmm0=*src
	PSHUFHW		xmm0, xmm0, 01bh				;reverzuje horni 4 wordy
	PSHUFLW		xmm0, xmm0, 01bh				;reverzuje spodni 4 wordy
	pshufd		xmm0, xmm0, 04eh				;prohodi horni a dolni int64, tim jsou reverzovany wordy
    movups		dword ptr[rcx+r8], xmm0			;dst[r8]=xmm0

	add			rdx, 16
ReverseInt16_8Impl2:
    sub			r8, 16
    jns			ReverseInt16_8Impl1
	ret 
ReverseInt16_8Impl ENDP 

;void Copy_16Impl(char* dst, char* src, int len);
;kopiruje pole char, ze zdroje src kopiruje do dst
;delka vstupniho a vystupniho pole je len a musi byt v nasobcich 16
; r8 -len
; rdx - src
; rcx - dst 

Copy_16Impl PROC 
	jmp			Copy_16Impl2
Copy_16Impl1:
	movups		xmm0, dword ptr [rdx+r8]			;xmm0=*src
    movups		dword ptr[rcx+r8], xmm0			;*dst=xmm0
Copy_16Impl2:
    sub			r8, 16
    jns			Copy_16Impl1
	ret 
Copy_16Impl ENDP 


; void CopyBGR24ToBGR32Imp(BGR32* dst, BGR* src, int len);
; kopiruje pole BGR do pole BGR32
; delka vstupniho a vystupniho pole je len a musi byt v nasobcich 4
; r8 -len
; rdx - src
; rcx - dst 

CopyBGR24ToBGR32Impl PROC 
	mov			r9, r8
	shl			r8, 1
	add			r9, r8
	shl			r8, 1
	movups		xmm1, dword ptr [ConstCopyBGRToBGR32]
	jmp			CopyBGR24ToBGR32Impl2
CopyBGR24ToBGR32Impl1:
	movups		xmm0, dword ptr [rdx+r9]			;xmm0=*src
	PSHUFB		xmm0, xmm1							;zprehazi bajty BGR->BGR32 a to 4x
    movups		dword ptr[rcx+r8], xmm0			;*dst=xmm0
CopyBGR24ToBGR32Impl2:
    sub			r9, 12
    sub			r8, 16
    jns			CopyBGR24ToBGR32Impl1
	ret 
CopyBGR24ToBGR32Impl ENDP 



; void CopyRGB24ToBGR32Imp(BGR32* dst, RGB* src, int len);
; kopiruje pole RGB do pole BGR32
; delka vstupniho a vystupniho pole je len a musi byt v nasobcich 4
; r8 -len
; rdx - src
; rcx - dst 

CopyRGB24ToBGR32Impl PROC 
	mov			r9, r8
	shl			r8, 1
	add			r9, r8
	shl			r8, 1
	movups		xmm1, dword ptr [ConstCopyRGBToBGR32]
	jmp			CopyRGB24ToBGR32Impl2
CopyRGB24ToBGR32Impl1:
	movups		xmm0, dword ptr [rdx+r9]			;xmm0=*src
	PSHUFB		xmm0, xmm1							;zprehaci bajty RGB->BGR32 a to 4x
    movups		dword ptr[rcx+r8], xmm0			;*dst=xmm0
CopyRGB24ToBGR32Impl2:
    sub			r9, 12
    sub			r8, 16
    jns			CopyRGB24ToBGR32Impl1
	ret 
CopyRGB24ToBGR32Impl ENDP 

;void ReverseRGB24ToBGR32(BGR32* dst, RGB* src, int len);
;reverzuje pole Int16, ze zdroje src kopiruje do dst
;delka vstupniho a vystupniho pole je len a musi byt v nasobcich 4
; r8 -len
; rdx - src
; rcx - dst 

ReverseRGB24ToBGR32Impl PROC 
	shl			r8, 2			;r8=len*2
	movups		xmm1, dword ptr[ConstReverseRGBToBGR32]
	jmp			ReverseRGB24ToBGR32Impl2
ReverseRGB24ToBGR32Impl1:
	movups		xmm0, dword ptr [rdx]			;xmm0=*src
	PSHUFB		xmm0, xmm1						;zprehaci bajty RGB->BGR32 a to 4x v reverznim poradi
    movups		dword ptr[rcx+r8], xmm0		;dst[ebx]=xmm0

	add			rdx, 12
ReverseRGB24ToBGR32Impl2:
    sub			r8, 16
    jns			ReverseRGB24ToBGR32Impl1
	ret 
ReverseRGB24ToBGR32Impl ENDP 


ENDIF

END 

