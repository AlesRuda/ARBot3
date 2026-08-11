IFDEF RAX
else

;.586              ;Target processor.  Use instructions for Pentium class machines
.MODEL FLAT, C    ;Use the flat memory model. Use C calling conventions
;.STACK            ;Define a stack segment of 1KB (Not required for this example)
.DATA             ;Create a near data segment.  Local variables are declared after
                  ;this directive (Not required for this example)


Const0			REAL4  0.0f
Const1			REAL4  1.0f
Const32			REAL4  32.0f
Const_1			REAL4  -1.0f
Const1m			REAL4  0.001f
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


;void TransformPoint4DImpl(Point4D* dst, float* transform, Point4D* src, int len);
;pole vektoru Point4D vynasobi matici transform a vysledek ulozi dst
;delka vstupniho a vystupniho pole je len
; ebp+16 -len
; ebp+12 - src
; ebp+8 - transform
; ebp+4 - dst

TransformPoint4DImpl PROC EXPORT
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		ebx, [ebp+16]	;ebx=len
	shl		ebx, 4			;ebx<<=4, 16 je pocet bajtu struktury Point4D tj. 4*float
	mov		esi, [ebp+12]	;esi=src
	mov		edx, [ebp+8]	;edx=transform
	mov		edi, [ebp+4]	;edi=dst

	movups xmm0, [edx]
    movups xmm1, 16[edx]
    movups xmm2, 32[edx]
    movups xmm3, 48[edx]
	jmp TransformPoint4DImpl2

TransformPoint4DImpl1:
     movups xmm4, [esi+ebx]			;xmm4=src[ebx]

	 pshufd  xmm5, xmm4, 000h		;vsechny 4 floaty xmm5 obsahuji xmm4[31:0]
     mulps xmm5, xmm0				;xmm5*=xmm0, 4 spodni floaty

	 pshufd  xmm6, xmm4, 055h		;vsechny 4 floaty xmm6 obsahuji xmm4[63:32]
     mulps xmm6, xmm1				;xmm6*=xmm1, 4 spodni floaty
     addps xmm5, xmm6				;xmm5+=xmm6, 4 spodni floaty

	 pshufd  xmm6, xmm4, 0aah		;vsechny 4 floaty xmm6 obsahuji xmm4[95:64]
     mulps xmm6, xmm2				;xmm6*=xmm2, 4 spodni floaty
     addps xmm5, xmm6				;xmm5+=xmm6, 4 spodni floaty

	 pshufd  xmm6, xmm4, 0ffh		;vsechny 4 floaty xmm6 obsahuji xmm4[127:96]
     mulps xmm6, xmm3				;xmm6*=xmm2, 4 spodni floaty
     addps xmm5, xmm6				;xmm5+=xmm6, 4 spodni floaty

     movups [edi+ebx], xmm5			;dst[ebx]=xmm5
TransformPoint4DImpl2:
     sub	ebx, 16
     jns TransformPoint4DImpl1
	popad
	ret 
TransformPoint4DImpl ENDP 


;z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste dopprava, y - roste dolu a z od kamery)
;transform je pole vektoru xy, plati xyz=[x*dist, y*dist, dist], pole transform a src obsahuje len prvku,
;nektere hodnoty v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
;funkce vraci pocet zapsanych zaznamu do dst
; ebp+16 -len
; ebp+12 - transform
; ebp+8 - dist
; ebp+4 - dst


Depth2XYZImpl PROC EXPORT
;LOCAL const1:DWORD
 	mov		eax, esp
	push EDI
	push ESI
	push EBP
	push EBX
	push ECX
	push EDX

	mov			ebp, eax
	mov			ebx, [ebp+16]	;ebx=len
    sub			ebx, 1
	mov			edi, [ebp+12]	;edi=transform
	mov			esi, [ebp+8]	;esi=dist
	mov			ecx, [ebp+4]	;ecx=dst

	movss		xmm1, dword ptr [Const1]
    pshufd		xmm0, xmm1, 000h		;vsechny 4 floaty xmm0 obsahuji xmm1[31:0]
	movss		xmm6, dword ptr [Const1m]

	mov			eax, 0
Depth2XYZ_1:
	movsx		edx, word ptr [esi+2*ebx]		;edx=dist[ebx]
	cmp			edx, 0
	jle			Depth2XYZ_2						;vzdalenost 0 preskocit
	CVTSI2SS 	xmm0, edx					;xmm0=dist[ebx] 
	mulss		xmm0, xmm6					;xmm0=0.001*dist[ebx] 
    pshufd		xmm3, xmm0, 040h			;xmm3 3 nejnizsi floaty = dist[ebx], nejvyssi =1

	movq		xmm5, qword ptr [edi+8*ebx]		;xmm4 =transfer[ebx] (y, x)
    shufps		xmm5, xmm1, 004h		;xmm5=(1, 1, y, x)

	mulps		xmm5, xmm3				;xmm5*=xmm3, 4 spodni floaty, nejnizsi by mel byt 1

	movups		[ecx], xmm5			;dst[ecx]=xmm5
	add			ecx, 16
	inc			eax
Depth2XYZ_2:

	sub			ebx, 1
	jge			Depth2XYZ_1

	pop			EDX
	pop			ECX
	pop			EBX
	pop			EBP
	pop			ESI
	pop			EDI
	ret 
Depth2XYZImpl ENDP 







;z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste doprava, y - roste dolu a z od kamery)
;nasledne bod pootoci v prostoru
;transform je pole vektoru xy, plati xyz=[x*dist, y*dist, dist], pole transform a src obsahuje len prvku,
;nektere hodnoty v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
;funkce vraci pocet zapsanych zaznamu do dst

;pole vektoru Point4D vynasobi matici transform a vysledek ulozi dst
;delka vstupniho a vystupniho pole je len
; ebp+20 -len
; ebp+16 - dist
; ebp+12 - rotate
; ebp+8 - transform
; ebp+4 - dst

DepthTransformImpl PROC EXPORT
 	mov			eax, esp
	push		EDI
	push		ESI
	push		EBP
	push		EBX
	push		ECX
	push		EDX
	mov			ebp, eax
	mov			ebx, [ebp+20]	;ebx=len
	mov			esi, [ebp+16]	;esi=dist
	mov			edx, [ebp+12]	;edx=rotate

	movups		xmm0, [edx]
    movups		xmm1, 16[edx]
    movups		xmm2, 32[edx]
    movups		xmm3, 48[edx]				; dx - dale uz rotate neni potreba
	movups		xmm4, dword ptr [DepthTransformImplConst]	; xmm4=(1, 0.001, 0.001, 0.001), prevod mm na m
    mulps		xmm0, xmm4					;xmm0*=(1, 0.001, 0.001, 0.001)
    mulps		xmm1, xmm4					;xmm1*=(1, 0.001, 0.001, 0.001)
    mulps		xmm2, xmm4					;xmm2*=(1, 0.001, 0.001, 0.001)

	mov			edi, [ebp+8]				;edi=transform
	mov			ecx, [ebp+4]				;ecx=dst

	movss		xmm4, dword ptr [Const1]
	pshufd		xmm5, xmm4, 000h			; vsechny 4 floaty xmm5 obsahuji 1

	mov			eax, 0						; pocitadlo zapisu
	jmp			DepthTransformImpl_2

DepthTransformImpl_1:
	movsx		edx, word ptr [esi+2*ebx]	;edx=dist[ebx]
	cmp			edx, 0
	jle			DepthTransformImpl_2		;vzdalenost <=0 preskocit

	CVTSI2SS 	xmm5, edx					;xmm5=(1, 1, 1, (float)dist[edx])

	pshufd		xmm6, xmm5, 0c0h			;xmm6=(1, dist[edx], dist[edx], dist[edx])
	movq		xmm7, qword ptr [edi+8*ebx]	;xmm7 =transfer[ebx] (y, x)
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

	movups		[ecx], xmm6					;dst[ecx]=xmm6
	add			ecx, 16
	inc			eax
DepthTransformImpl_2:
	sub			ebx, 1
	jge			DepthTransformImpl_1

	pop			EDX
	pop			ECX
	pop			EBX
	pop			EBP
	pop			ESI
	pop			EDI
	ret 
DepthTransformImpl ENDP 













;Prolozi rovinou mnozinu bodu jejich z je meni jak MaxZ.
;Lepe receno spocte parametry pro vypocet prolozeni
; r9d - len
; xmm2 - MaxZ
; rdx - Point4D* src 
; rcx - PlaneParams* param - sum(1), sum(z), sum(y), sum(x), sum(1), sum(z*x), sum(y*z), sum(x*y), sum(1), sum(z*z), sum(y*y), sum(x*x)


; ebp+16 -len
; ebp+12 - MaxZ
; ebp+8 - Point4D* src 
; ebp+4 - param


;xmm0 = suma (1, z, y, x)
;xmm1 = suma (1, z*x, y*z, x*y)
;xmm2 = suma (1, z*z, y*y, x*x)

XYZ2PlaneImpl PROC EXPORT
 	mov		eax, esp
	push	EDI
	push	ESI
	push	EBP
	push	EBX
	push	ECX
	push	EDX

	mov		ebp, eax
	mov		esi, [ebp+16]	;ebx=len
	cmp		esi, 0			;pokud len==0 return
	je		XYZ2Plane3

	shl		esi, 4			;esi<<=4, 16 je pocet bajtu struktury Point4D tj. 4*float
	movss	xmm5, dword ptr [ebp+12]	;xmm5=MaxX
	mov		edx, [ebp+8]	;esi=src
	mov		ecx, [ebp+4]	;ecx=param

	movups	xmm0, [ecx]
	movups	xmm1, [ecx+16]
	movups	xmm2, [ecx+32]

XYZ2Plane1:
	movups  xmm3, [edx+esi-16]	;xmm3=src[si-1]
	pextrd	eax, xmm3, 2		;eax=src[si-1].z
	movd	xmm4, eax			;xmm4=src[si-1].z

	xorps	xmm6, xmm6			;xmm6=0
	subss	xmm6, xmm4			;xmm6=xmm6-xmm4
	maxss	xmm4, xmm6			;xmm4=max(xmm4, xmm6)=max(xmm4, 0-xmm4)=abs(xmm4)

	ucomiss	xmm4, xmm5
	jnc		XYZ2Plane2			;skoc kdyz xmm4>xmm5
	addps 	xmm0, xmm3			;xmm0+=xmm3

	pshufd	xmm4, xmm3, 0c9h	;xmm4 = (1, x, z, y)
	mulps 	xmm4, xmm3			;xmm4*=xmm3, xmm4=(1, z*x, y*z, x*y)
	addps 	xmm1, xmm4			;xmm1+=xmm4
	mulps 	xmm3, xmm3			;xmm3*=xmm3, xmm4=(1, z*z, y*y, x*x)
	addps 	xmm2, xmm3			;xmm2+=xmm3
XYZ2Plane2:
	sub		esi, 16
	jne		XYZ2Plane1

	movups	[ecx], xmm0
	movups	[ecx+16], xmm1
	movups	[ecx+32], xmm2

XYZ2Plane3:
	pop		EDX
	pop		ECX
	pop		EBX
	pop		EBP
	pop		ESI
	pop		EDI
	ret 
XYZ2PlaneImpl ENDP 


; void BackProjectImpl(char* probability, BGR* img, char* backProjectTab, int len);
; Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
; ebp+16 -len
; ebp+12 - backProjectTab
; ebp+8 - img
; ebp+4 - probability

BackProjectImpl PROC EXPORT
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		ecx, [ebp+16]	;ecx=len
	mov		esi,  [ebp+12]	;esi=backProjectTab
	mov		edx, [ebp+8]	;edx=img
	mov		edi, [ebp+4]	;edi=probability

BackProjectImpl1:
	mov		eax, dword ptr[edx] ;eax=img[edx]
	and		eax, 0f0f0f0h		;eax&=0xf0f0f0
	shr		eax, 4				;eax>>=4
	and		ebx, 0				;ebx=0
	or		bh, al				;bx=((img[esi]&0xf0)<<4)
	shr		eax, 4				;eax>>=4
	or		bl, al				;bx|=((img[esi]&0xf000)>>8)
	shr		eax, 4				;eax>>=4
	or		bl, ah				;bx|=((img[esi]&0xf00000)>>20)
	mov		al, byte ptr[esi+ebx] ;al=bp[ebx]
	mov		byte ptr[edi], al  ;dst[edi]=al
	add		edx, 3
	inc		edi
	dec		ecx
	jne		BackProjectImpl1

	popad
	ret 
BackProjectImpl ENDP 

; void BackProjectBGR32Impl(char* probability, BGR* img, char* backProjectTab, int len);
; Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
; ebp+16 -len
; ebp+12 - backProjectTab
; ebp+8 - img
; ebp+4 - probability

BackProjectBGR32Impl PROC EXPORT
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		ecx, [ebp+16]	;ecx=len
	mov		esi,  [ebp+12]	;esi=backProjectTab
	mov		edx, [ebp+8]	;edx=img
	mov		edi, [ebp+4]	;edi=probability

BackProjectBGR32Impl1:
	mov		eax, dword ptr[edx] ;eax=img[edx]
	and		eax, 0f0f0f0h		;eax&=0xf0f0f0
	shr		eax, 4				;eax>>=4
	and		ebx, 0				;ebx=0
	or		bh, al				;bx=((img[esi]&0xf0)<<4)
	shr		eax, 4				;eax>>=4
	or		bl, al				;bx|=((img[esi]&0xf000)>>8)
	shr		eax, 4				;eax>>=4
	or		bl, ah				;bx|=((img[esi]&0xf00000)>>20)
	mov		al, byte ptr[esi+ebx] ;al=bp[ebx]
	mov		byte ptr[edi], al  ;dst[edi]=al
	add		edx, 4
	inc		edi
	dec		ecx
	jne		BackProjectBGR32Impl1

	popad
	ret 
BackProjectBGR32Impl ENDP 



;void ClearAggregateImpl(AggregateItem* ais, int32 *uais, int cnt);
; Inizializuje pouzite agregacni itemy nastavenim Count na 0
; a je pole ukazatelu na agregacni itemy
; delka vstupniho a vystupniho pole je len
; ebp+12 -len
; ebp+8 - uais
; ebp+4 - ais

ClearAggregateImpl PROC EXPORT
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		ebx, [ebp+12]	;ebx=len
 	mov		esi, [ebp+8]	;esi=uais
 	mov		edi, [ebp+4]	;edi=ais
	mov		edx, dword ptr [Const0] ;edx=(float)0
    jmp     ClearAggregateImpl_2

ClearAggregateImpl_1:
     mov   ecx, dword ptr[esi+4*ebx]		;ecx=uais[ebx]
     mov   (AggregateItem ptr[edi+ecx]).Count, edx	;ais[uais[ebx]]->Count=0
ClearAggregateImpl_2:

     sub   ebx, 1
     jge   ClearAggregateImpl_1
	popad
	ret 
ClearAggregateImpl ENDP 




; int ExtractObstacles(Aggregateitem* ais, int32* uais, int len, Point4D* ops, float minCount, float minStd2)
; ebp+24 - minStd2
; ebp+20 - minCount
; ebp+16 - ops
; ebp+12 - len
; ebp+8 - uais
; ebp+4 - ais

ExtractObstaclesImpl PROC EXPORT
 	mov		eax, esp
	push	esi
	push	ebx
	push	edi
	push	ebp
	mov		ebp, eax

	xor		eax, eax		;eax=0

	mov		esi, [ebp+8]	;esi=uais
	mov		ebx, [ebp+12]	;ebx=len
	mov		edi, [ebp+16]	;edi=ops
	movss	xmm0, dword ptr [ebp+20]	;xmm0=minCount
	movss	xmm1, dword ptr [ebp+24]	;xmm1=minStd2
	movss	xmm2, dword ptr [Const1]	;xmm2=(0, 0, 0, 1)
	mov		ebp, [ebp+4]	;ebp=ais

	jmp		ExtractObstaclesImpl2

ExtractObstaclesImpl1:
	mov		ecx, dword ptr[esi+4*ebx] ;ebp=uias[ebx]
;varianta AggregateItem SumX, SumY, SumZ, Count, SumZ2
	movups	xmm3, [ebp+ecx]			;xmm3=(Count, SumZ, SumY, SumX)
	pshufd  xmm4, xmm3, 0ffh		;vsechny 4 floaty xmm4 obsahuji ais[uias[ebx]]->Count

	comiss  xmm4, xmm0
	jbe		ExtractObstaclesImpl2	;jmp pokud je ais[uias[ebx]]->Count<=minCount
	divps	xmm3, xmm4			;xmm3=(1, SumZ/Count, SumY/Count, SumX/Count)

	movss	xmm5, (AggregateItem ptr[ebp+ecx]).SumZ2	;xmm5=ais[uias[ebx]]->SumZ2
	divss	xmm5, xmm4			;xmm5=SumZ2/Count

	pshufd  xmm6, xmm3, 2		;xmm6=SumZ/Count
	mulss	xmm6, xmm6
	subss	xmm5, xmm6			;xmm5=SumZ2/ais[uias[ebx]]->Count-(SumZ/ais[uias[ebx]]->Count)^2

	comiss  xmm5, xmm1			;
	jbe		ExtractObstaclesImpl2	;jmp pokud je xmm5<xmm1 tj. std(z)^2<=minStd2

	movups	[edi], xmm3			;ops[eax]->(w, z, y, x)=xmm3=(1, SumZ/Count, SumY/Count, SumX/Count)

	inc		eax
	add		edi, 16

ExtractObstaclesImpl2:
	dec		ebx
	jns		ExtractObstaclesImpl1

	pop		ebp
	pop		edi
	pop		ebx
	pop		esi

	ret 
ExtractObstaclesImpl ENDP 


; int AggregateObstaclesImpl(Point4D* worldPoints, int worldPointsCount, double r, int xOff, int yOff, AggregateItem* ais, int32* uais, int width, int height, Point4D *v)

; ebp+40 - v
; ebp+36 - height
; ebp+32 - width
; ebp+28 - uais
; ebp+24 - ais

; ebp+20 - yOff
; ebp+16 - xOff
; ebp+12 - r
; ebp+8 - worldPointsCount
; ebp+4 - worldPoints

AggregateObstaclesImpl PROC EXPORT
 	mov			eax, esp
	push		esi
	push		ebx
	push		edi
	push		ebp
	mov			ebp, eax

	xor			eax, eax		;eax=0

	mov			esi, [ebp+4]	;esi=worldPoints
	mov			ebx, [ebp+8]	;ebx=worldPointsCount
	shl			ebx, 4			;ebx*=16, 16 je velikost Point4D
	movss		xmm0, dword ptr [ebp+12]	;xmm0=r
    shufps		xmm0, xmm0, 0				;vsechny 4 floaty xmm0 obsahuji r
	movd		xmm1, dword ptr [ebp+16]	;xmm1=xOff
	movd		xmm2, dword ptr [ebp+20]	;xmm2=yOff
	insertps	xmm1, xmm2, 010h			;xmm1=(0, 0, yOff, xOff)
	CVTDQ2PS	xmm1, xmm1					;prevede xmm1 na floaty
	 
	mov			edi, [ebp+24]	;edi=ais
	mov			ecx, [ebp+28]	;ecx=uais
	movd		xmm2, dword ptr [ebp+32]	;xmm2=width

	movd		xmm4, [Const1d]				;xmm4=(0, 0, 0, 1)
	insertps	xmm4, xmm2, 010h			;xmm4=(0, 0, width, 1)
	CVTDQ2PS	xmm4, xmm4					;prevede xmm4 na floaty
	movss		xmm3, [Const32]
    shufps		xmm3, xmm3, 0				;vsechny 4 floaty xmm3 obsahuji 32
	mulps		xmm4, xmm3					;xmm4=(0, 0, 32*width, 32)

	movd		xmm3, dword ptr [ebp+36]	;xmm3=height
	insertps	xmm2, xmm3, 010h			;xmm2=(0, 0, height, width)
	CVTDQ2PS	xmm2, xmm2					;prevede xmm2 na floaty
	movups		xmm3, [ebp+40]				;xmm3=v=(d, c, b, a)


	jmp		AggregateObstaclesImpl2

AggregateObstaclesImpl1:
	movups	xmm7, [esi+ebx]			;xmm7=(1, z, y, x)=worldPoints[ebx]
	movaps	xmm6, xmm7
	divps	xmm6, xmm0				;xmm6=worldPoints[ebx]/r
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
	CVTSS2SI edx, xmm5				;edx=index do ais
	movaps	xmm6, xmm7				;xmm6=(1, z, y, x)
	mulps	xmm6, xmm3				;xmm6=(d, c*z, b*y, a*x)
	haddps	xmm6, xmm6				;xmm6=(0, 0, d+c*z, b*y+a*x)
	haddps	xmm6, xmm6				;xmm6=(0, 0, 0, d+c*z+b*y+a*x) tj nove z'
	insertps	xmm7, xmm6, 020h		;xmm7=(1, z', y, x)
	mulss	xmm6, xmm6				;xmm6=z'^2

	movss	xmm5, (AggregateItem ptr[edi+edx]).Count			;xmm5=ais[edx]
	comiss	xmm5, [Const0]			;xmm5==0
	je		AggregateObstaclesImpl3 ;ano 

	addss	xmm6, (AggregateItem ptr[edi+edx]).SumZ2	;xmm6+=ais[edx].SumZ2
	movss	(AggregateItem ptr[edi+edx]).SumZ2, xmm6	;ais[edx].SumZ2=xmm6
	movups	xmm6, dword ptr[edi+edx]			;xmm6=ais[edx]
	addps	xmm7, xmm6							;xmm7+=ais[edx], pouziva se scitani pres register XMM^, aby mohl byt nacten z lokace ktera neni zarovnana na 16 nasobek
	movups	dword ptr[edi+edx], xmm7			;ais[edx]=xmm7

	jmp		AggregateObstaclesImpl2


AggregateObstaclesImpl3:
	movups	dword ptr[edi+edx], xmm7			;ais[edx]=xmm7=(1, z', y, x)
	movss	(AggregateItem ptr[edi+edx]).SumZ2, xmm6			;ais[edx].SumZ2=xmm6=z'^2
	mov		dword ptr[ecx+4*eax], edx			;*uais=edx tj index do ais

	inc		eax

AggregateObstaclesImpl2:
	sub		ebx, 16
	jns		AggregateObstaclesImpl1

	pop		ebp
	pop		edi
	pop		ebx
	pop		esi

	ret 
AggregateObstaclesImpl ENDP 



;void ReverseInt16_8(int16* dst, int16* src, int len);
;reverzuje pole Int16, ze zdroje src kopiruje do dst
;delka vstupniho a vystupniho pole je len a musi byt v nasobcich 8
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst 

ReverseInt16_8Impl PROC 
 	mov			eax, esp
	pushad
	mov			ebp, eax
	mov			ebx, [ebp+12]	;ebx=len
	mov			esi, [ebp+8]	;esi=src
	mov			edi, [ebp+4]	;edi=dst
	shl			ebx, 1			;ebx=len*2
	jmp			ReverseInt16_8Impl2
ReverseInt16_8Impl1:
	movups		xmm0, dword ptr [esi]			;xmm0=*src
	PSHUFHW		xmm0, xmm0, 01bh				;reverzuje horni 4 wordy
	PSHUFLW		xmm0, xmm0, 01bh				;reverzuje spodni 4 wordy
	pshufd		xmm0, xmm0, 04eh				;prohodi horni a dolni int64, tim jsou reverzovany wordy
    movups		dword ptr[edi+ebx], xmm0		;dst[ebx]=xmm0

	add			esi, 16
ReverseInt16_8Impl2:
    sub			ebx, 16
    jns			ReverseInt16_8Impl1
	popad
	ret 
ReverseInt16_8Impl ENDP 


;void Copy_16Impl(char* dst, char* src, int len);
;kopiruje pole char, ze zdroje src kopiruje do dst
;delka vstupniho a vystupniho pole je len a musi byt v nasobcich 16
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst 

Copy_16Impl PROC 
 	mov			eax, esp
	pushad
	mov			ebp, eax
	mov			ebx, [ebp+12]	;ebx=len
	mov			esi, [ebp+8]	;esi=src
	mov			edi, [ebp+4]	;edi=dst
	jmp			Copy_16Impl2
Copy_16Impl1:
	movups		xmm0, dword ptr [esi+ebx]			;xmm0=*src
    movups		dword ptr[edi+ebx], xmm0			;*dst=xmm0
Copy_16Impl2:
    sub			ebx, 16
    jns			Copy_16Impl1
	popad
	ret 
Copy_16Impl ENDP 




;void ReverseRGB24ToBGR32(BGR32* dst, RGB* src, int len);
;reverzuje pole Int16, ze zdroje src kopiruje do dst
;delka vstupniho a vystupniho pole je len a musi byt v nasobcich 4
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst 

ReverseRGB24ToBGR32Impl PROC 
 	mov			eax, esp
	pushad
	mov			ebp, eax
	mov			ebx, [ebp+12]	;ebx=len
	mov			esi, [ebp+8]	;esi=src
	mov			edi, [ebp+4]	;edi=dst
	shl			ebx, 2			;ebx=len*2
	movups		xmm1, dword ptr[ConstReverseRGBToBGR32]
	jmp			ReverseRGB24ToBGR32Impl2
ReverseRGB24ToBGR32Impl1:
	movups		xmm0, dword ptr [esi]			;xmm0=*src
	PSHUFB		xmm0, xmm1						;zprehaci bajty RGB->BGR32 a to 4x v reverznim poradi
    movups		dword ptr[edi+ebx], xmm0		;dst[ebx]=xmm0

	add			esi, 12
ReverseRGB24ToBGR32Impl2:
    sub			ebx, 16
    jns			ReverseRGB24ToBGR32Impl1
	popad
	ret 
ReverseRGB24ToBGR32Impl ENDP 





; void CopyRGB24ToBGR32Imp(BGR32* dst, RGB* src, int len);
; kopiruje pole RGB do pole BGR32
; delka vstupniho a vystupniho pole je len a musi byt v nasobcich 4
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst 

CopyRGB24ToBGR32Impl PROC 
 	mov			eax, esp
	pushad
	mov			ebp, eax
	mov			ebx, [ebp+12]	;ebx=len
	mov			ecx, ebx
	shl			ebx, 1
	add			ecx, ebx
	shl			ebx, 1
	mov			esi, [ebp+8]	;esi=src
	mov			edi, [ebp+4]	;edi=dst
	movups		xmm1, dword ptr [ConstCopyRGBToBGR32]
	jmp			CopyRGB24ToBGR32Impl2
CopyRGB24ToBGR32Impl1:
	movups		xmm0, dword ptr [esi+ecx]			;xmm0=*src
	PSHUFB		xmm0, xmm1							;zprehaci bajty RGB->BGR32 a to 4x
    movups		dword ptr[edi+ebx], xmm0			;*dst=xmm0
CopyRGB24ToBGR32Impl2:
    sub			ecx, 12
    sub			ebx, 16
    jns			CopyRGB24ToBGR32Impl1
	popad
	ret 
CopyRGB24ToBGR32Impl ENDP 


; void CopyBGR24ToBGR32Imp(BGR32* dst, RGB* src, int len);
; kopiruje pole BGR do pole BGR32
; delka vstupniho a vystupniho pole je len a musi byt v nasobcich 4
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst 

CopyBGR24ToBGR32Impl PROC 
 	mov			eax, esp
	pushad
	mov			ebp, eax
	mov			ebx, [ebp+12]	;ebx=len
	mov			ecx, ebx
	shl			ebx, 1
	add			ecx, ebx
	shl			ebx, 1
	mov			esi, [ebp+8]	;esi=src
	mov			edi, [ebp+4]	;edi=dst
	movups		xmm1, dword ptr [ConstCopyRGBToBGR32]
	jmp			CopyBGR24ToBGR32Impl2
CopyBGR24ToBGR32Impl1:
	movups		xmm0, dword ptr [esi+ecx]			;xmm0=*src
	PSHUFB		xmm0, xmm1							;zprehaci bajty RGB->BGR32 a to 4x
    movups		dword ptr[edi+ebx], xmm0			;*dst=xmm0
CopyBGR24ToBGR32Impl2:
    sub			ecx, 12
    sub			ebx, 16
    jns			CopyBGR24ToBGR32Impl1
	popad
	ret 
CopyBGR24ToBGR32Impl ENDP 










IF 0



; Hleda okraje vozovky 
; ebp+16 -height
; ebp+12 - width
; ebp+8 - img, propability
; ebp+4 - dst, &struct(mini, maxi, sum)

PathEdgeImpl PROC 
LOCAL	yaba:DWORD, daba:DWORD, do:DWORD
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		esi, [ebp+8]	;esi=img, probability
	mov		edi, [ebp+4]	;edi=dst, &struct(left, right, sum)
	
PathEdgeImpl1:
	mov		r10d, 0			;r10=sumVal = 0;
	mov		r11d, 0			;x=0
	mov		r15d, [ebp+12]	;i=width
	mov		r12d, 080000000h	;r12=sumLeftMax = int.MinValue;
	mov		r13d, 07fffffffh	;r13=sumRightMin = int.MaxValue;
	mov		r14d, 0			;r14=sum = 0;
	mov		ebx, -1			;ebx (left) = -1;
	mov		ecx, -1			;ecx (right) = -1;
PathEdgeImpl2:
	mov		ah, byte ptr[esi] ;al=*img
	mov		al, 128
	sub		al, ah			;al=128-*img
	jnc		PathEdgeImpl3	;*img>128 ? ne - skok
	inc		r14d			;sum++
PathEdgeImpl3:
	add		r10d, al			;sumVal += 128 - *img;
	cmp		r10d, r12d
	jng		PathEdgeImpl4
	mov		r12d, r10d		;sumLeftMax=sumVal
	mov		ebx, r11d		;left=x
PathEdgeImpl4:
	cmp		r10d, r13d
	jnl		PathEdgeImpl5
	mov		r13d, r10d		;sumRightMin=sumVal
	mov		ecx, r11d		;right=x
PathEdgeImpl5:
	inc		esi				;esi++, img++

	dec		r15d				;i --
	jne		PathEdgeImpl2	;skoc pokud i!=0

	mov		[edi], ebx		;dst->left=left
	mov		[edi+4], ecx	;dst->right=right
	mov		[edi+8], r14d	;dst->sum=sum

	add		edi, 12			;dalsi dst

	mov		eax, [ebp+16]	;eax=height
	dec		eax				;height--
	mov		[ebp+16], eax	;height=eax
	jne		PathEdgeImpl1

	popad
	ret 
PathEdgeImpl ENDP 





endif


;void Test1Impl(void* dst, void* src, int len);
;pole vektoru Point4D vynasobi matici transform a vysledek ulozi dst
;delka vstupniho a vystupniho pole je len
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst

Test1Impl PROC 
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		ebx, [ebp+12]	;ebx=len
    sub		ebx, 1
	mov		esi, [ebp+8]	;esi=src
	mov		edi, [ebp+4]	;edi=dst

Test1Impl_1:
     mov   al, byte ptr[esi+ebx]		;ecx=src[ebx]
     mov   byte ptr[edi+ebx], al		;dst[ebx]=ecx
;     mov   al, ah
 ;    mov   ah, al
     sub   ebx, 1
     jge   Test1Impl_1
	popad
	ret 
Test1Impl ENDP 



;void TestImpl(void* dst, void* src, int len);
;pole vektoru Point4D vynasobi matici transform a vysledek ulozi dst
;delka vstupniho a vystupniho pole je len
; ebp+12 -len
; ebp+8 - src
; ebp+4 - dst

TestImpl PROC 
 	mov		eax, esp
	pushad
	mov		ebp, eax
	mov		ebx, [ebp+12]	;ebx=len
    sub		ebx, 1
	mov		esi, [ebp+8]	;esi=src
	mov		edi, [ebp+4]	;edi=dst

TestImpl_1:
     mov   ecx, dword ptr[esi+ebx]		;ecx=src[ebx]
     mov   dword ptr[edi+ebx], ecx		;dst[ebx]=ecx
     sub   ebx, 1
     jge   TestImpl_1
	popad
	ret 
TestImpl ENDP 













ENDIF

END 

