#include <iostream>

//#include "stdafx.h"

#include "stdio.h"
#include "string.h"
#include <time.h>
#include <stdlib.h>
#include <malloc.h>
#include <float.h>
#include <math.h>
#include <limits.h>
#include "native_funcs.hpp"


extern "C"
{

// Deklarace assemblerové funkce (musí mít stejný název v .asm i .S souboru)
	long long AsmAddProc(long long a, long long b);

// Spole?ná exportovaná funkce pro C#
	EXPORT_API long long NativeAdd(long long a, long long b)
	{
	    // Zavolá příslušný assembler (na Win x64, na Linuxu ARM)
	    return AsmAddProc(a, b);
	}

	EXPORT_API void* Alloc(int len)
	{
#if defined(_WIN32)
		return _aligned_malloc(len, 16);
#else
		return malloc(len);
#endif
	}
	EXPORT_API void Free(void* ptr)
	{
#if defined(_WIN32)
		_aligned_free(ptr);
#else
		free(ptr);
#endif
	}

	/*
		//Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
		EXPORT_API void BackProject(char* probability, BGR* img, char* backProjectTab, int len)
		{
			BackProjectImpl(probability, img, backProjectTab, len);
		}
		*/
	void InitAggregate(AggregateItem* agg, int cnt)
	{
		for (int i = 0; i < cnt; i++)
		{
			agg[i].Count = 0;
		}
	}

	//Alokuje potrebne struktury pro vypocet 3D segmentace 
	EXPORT_API ComputeInfo* ComputeAlloc(int maxPoints, int width, int height, int xOff, int yOff, float resolution)
	{
		ComputeInfo* ci = (ComputeInfo*)Alloc(sizeof(ComputeInfo));
		ci->Height = height;
		ci->Width = width;
		ci->xOff = xOff;
		ci->yOff = yOff;
		ci->Resolution = resolution;
		ci->Aggregates = (AggregateItem*)Alloc((ci->AggregatesCount = width * height) * sizeof(AggregateItem));
		ci->UsedAggregates = (int*)Alloc((ci->AggregatesCount + 1) * sizeof(int));
		ci->UsedAggregatesCount = 0;
		//		memset((void*)ci->UsedAggregates, 0, (ci->AggregatesCount + 1) * sizeof(int));
		ci->WordObstaclePoints = (Point4D*)Alloc(width * height * sizeof(Point4D));
		ci->MaxCameraPoints = maxPoints;
		ci->CameraPoints = (Point4D*)Alloc(maxPoints * sizeof(Point4D));
		ci->WordPoints = (Point4D*)Alloc(maxPoints * sizeof(Point4D));
		ci->ObstaclePoints = (Point4D*)Alloc(width * height * sizeof(Point4D));
		ci->WordObstaclePoints = (Point4D*)Alloc(width * height * sizeof(Point4D));

		InitAggregate(ci->Aggregates, ci->Width * ci->Height);
		return ci;
	}

	EXPORT_API void ComputeFree(ComputeInfo* ci)
	{
		Free(ci->CameraPoints);
		Free(ci->WordPoints);
		Free(ci->UsedAggregates);
		Free(ci->Aggregates);
		Free(ci->ObstaclePoints);
		Free(ci->WordObstaclePoints);
		Free(ci);
	}

#if defined(_WIN32)
	__declspec(align(16)) float matrix[16];
	__declspec(align(16)) Point4D points[10000];
	__declspec(align(16)) Point4D dst[10000];
	__declspec(align(16)) PlaneParams modelParams;
#else
	alignas(16) float matrix[16];
	alignas(16) Point4D points[10000];
	alignas(16) Point4D dst[10000];
	alignas(16) PlaneParams modelParams;
#endif

	// resetuje agrgovane udaje ve vypoctu aproximace bodu rovinou
	EXPORT_API void ResetPlaneParams(PlaneParams* p)
	{
		p->Count1 = 0;
		p->Count2 = 0;
		p->Count3 = 0;
		p->SumX = 0;
		p->SumY = 0;
		p->SumZ = 0;
		p->SumXY = 0;
		p->SumYZ = 0;
		p->SumZX = 0;
		p->SumXX = 0;
		p->SumYY = 0;
		p->SumZZ = 0;
	}

	// prepocte agregovane hodnoty pro prolozeni bodu rovinou na rovinu
	// vysledkem bude nastaveni atributu v
	EXPORT_API void CalcPlaneParams(PlaneParams* p)
	{
		float sx = p->SumX;
		float sy = p->SumY;
		float sz = p->SumZ;
		float sxz = p->SumZX;
		float sxy = p->SumXY;
		float syz = p->SumYZ;
		float sxx = p->SumXX;
		float syy = p->SumYY;
		float szz = p->SumZZ;
		int n = p->Count1;

		float d = (syy * sx * sx - 2 * sx * sxy * sy + n * sxy * sxy + sxx * sy * sy - n * sxx * syy);
		float a = (sxz * sy * sy + n * sxy * syz - n * sxz * syy - sx * sy * syz + sx * syy * sz - sxy * sy * sz) / d;
		float b = (sx * sx * syz + n * sxy * sxz - n * sxx * syz - sx * sxz * sy - sx * sxy * sz + sxx * sy * sz) / d;
		float c = (sxy * sxy * sz - sx * sxy * syz + sx * sxz * syy - sxy * sxz * sy + sxx * sy * syz - sxx * syy * sz) / d;

		p->v.x = -a;
		p->v.y = -b;
		p->v.z = 1;
		p->v.a = -c;
	}

	void ClearAggregate(AggregateItem* ais, int32_t* uais, int cnt)
	{
		for (int i = 0; i < cnt; i++)
			ais[uais[i] / sizeof(AggregateItem)].Count = 0;
	}

	int ExtractObstacles(AggregateItem* ais, int* uais, int len, Point4D* ops, float minCount, float minStd2)
	{
		Point4D* op;
		AggregateItem* ai;
		double cnt, std2, z;
		int idx = 0;

		for (int i = 0; i < len; i++)
		{
			ai = &ais[uais[i] / sizeof(AggregateItem)];
			cnt = ai->Count;
			if (cnt > minCount)
			{
				z = ai->SumZ / cnt;
				std2 = ai->SumZ2 / cnt - z * z;
				if (std2 > minStd2)
				{
					op = &ops[idx];
					op->x = ai->SumX / cnt;
					op->y = ai->SumY / cnt;
					op->z = z;
					op->a = 1;
					idx++;
				}
			}
		}
		return idx;
	}

	int AggregateObstacles(Point4D* wordPoints, int wordPointsCount, float r, int xOff, int yOff, AggregateItem* ais, int* uais, int width, int height, Point4D v)
	{
		float a = v.x;
		float b = v.y;
		float c = v.z;
		float d = v.a;

		int x;
		int y;
		float z;
		float z1;
		AggregateItem* ai;
		int usedAggregateCount = 0;
		int idx = 0;

		for (int i = 0; i < wordPointsCount; i++)
		{
			Point4D p = wordPoints[i];
			x = p.x / r + xOff;
			y = p.y / r + yOff;
			if (x >= 0 && x < width && y >= 0 && y < height)
			{
				z = a * p.x + b * p.y + c * p.z + d * p.a;
				//				z = p.z;

				ai = &ais[idx = x + y * width];
				if (ai->Count == 0)
				{
					uais[usedAggregateCount++] = idx * sizeof(AggregateItem);
					ai->SumX = p.x;
					ai->SumY = p.y;
					ai->SumZ = z;
					ai->SumZ2 = z * z;
					/*						z1 = z;
					z1 *= z;
					ai->Sum2 = z1;
					z1 *= z;
					ai->Sum3 = z1;
					z1 *= z;
					ai->Sum4 = z1;
					ai->Max = z;
					ai->Min = z;*/
				}
				else
				{
					ai->SumX += p.x;
					ai->SumY += p.y;
					ai->SumZ += z;
					ai->SumZ2 += z * z;
					/*
					z1 = z;
					z1 *= z;
					ai->Sum2 += z1;
					z1 *= z;
					ai->Sum3 += z1;
					z1 *= z;
					ai->Sum4 += z1;
					if (ai->Max < z)
						ai->Max = z;
					if (ai->Min > z)
						ai->Min = z;
						*/
				}
				ai->Count++;
			}
		}

		return usedAggregateCount;
	}


	EXPORT_API void Segment(ComputeInfo* ci, PlaneParams* params, short* dist,
		float* transformMatrix, Point2D* transform, int len, float maxZ)
	{
		if (ci != NULL && params != NULL && dist != NULL && transformMatrix != NULL && transform != NULL)
		{
			Point4D* cameraPoints = &(ci->CameraPoints[ci->CameraPointsCount]);
			Point4D* wordPoints = &(ci->WordPoints[ci->WordPointsCount]);

			int wordPointsCount;
			wordPointsCount = DepthTransformImpl(wordPoints, transform, transformMatrix, dist, len);
			ci->WordPointsCount += wordPointsCount;

			ResetPlaneParams(params);
			XYZ2PlaneImpl(params, wordPoints, maxZ, wordPointsCount);
			CalcPlaneParams(params);

			float r = ci->Resolution;
			int xOff = ci->xOff;
			int yOff = ci->yOff;
			int w = ci->Width;
			int h = ci->Height;

			int* uais = ci->UsedAggregates;
			AggregateItem* ais = ci->Aggregates;

			ci->UsedAggregatesCount = AggregateObstaclesImpl(wordPoints, wordPointsCount, r, xOff, yOff, ais, uais, w, h, (params->v));
		}
	}


	EXPORT_API void Segment2(ComputeInfo* ci,
		short* leftDist, float* leftTransformMatrix, Point2D* leftTransform,
		short* rightDist, float* rightTransformMatrix, Point2D* rightTransform,
		float* globalTransformMatrix,
		int len, float maxZ)
	{
		ClearAggregateImpl(ci->Aggregates, ci->UsedAggregates, ci->UsedAggregatesCount);
		ci->UsedAggregatesCount = 0;

		ci->CameraPointsCount = 0;
		ci->WordPointsCount = 0;

		Segment(ci, &ci->LeftCameraParams, leftDist, leftTransformMatrix, leftTransform, len, maxZ);
		Segment(ci, &ci->RightCameraParams, rightDist, rightTransformMatrix, rightTransform, len, maxZ);

		ci->ObstaclePointsCount = ExtractObstaclesImpl(ci->Aggregates, ci->UsedAggregates, ci->UsedAggregatesCount, ci->ObstaclePoints, 15, 0.0025);

		ci->WordObstaclePointsCount = 0;
		if (globalTransformMatrix != NULL)
		{
			TransformPoint4DImpl(ci->WordObstaclePoints, globalTransformMatrix, ci->ObstaclePoints, ci->WordObstaclePointsCount = ci->ObstaclePointsCount);
		}
	}


	EXPORT_API int FindPathEdge_old(PathEdge* dst, unsigned char* probability, int width, int height)
	{
		int minW = width / 10;
		int maxW = width * 9 / 10;
		int cnt = 0;
		int sum = 0;
		int sumVal = 0;
		int sumLeftMax = INT_MIN;
		int sumRightMin = INT_MAX;
		int left = -1;
		int right = -1;
		int v;
		int state;
		int lastState = -1;


		for (int y = 0; y < height; y++)
		{
			sum = 0;
			sumVal = 0;
			sumLeftMax = INT_MIN;
			sumRightMin = INT_MAX;
			left = -1;
			right = -1;

			v = *probability++;
			sum = (v >= 128) ? 1 : 0;
			sumVal = 128 - v;
			lastState = v >= 128;

			for (int x = 1; x < width; x++)
			{
				v = *probability++;
				state = v >= 128;
				if (state)
					sum++;
				sumVal += 128 - v;
				if (state != lastState)
				{
					if (sumVal > sumLeftMax)
					{
						sumLeftMax = sumVal;
						left = x;
					}
					if (sumVal < sumRightMin)
					{
						sumRightMin = sumVal;
						right = x;
					}
					lastState = state;
				}
			}

			if (sum > minW && sum < maxW)
			{
				if (sumRightMin > sumVal)
					right = -1;
				if (left != -1 && right != -1)
				{
					if (left < right)
					{
						dst->Left = left;
						dst->Right = right;
						dst->Y = y;

						dst++;
						cnt++;
					}
				}
				else
				{
					dst->Left = left;
					dst->Right = right;
					dst->Y = y;

					dst++;
					cnt++;
				}
			}
		}
		return cnt;
	}

	EXPORT_API int FindPathEdge(PathEdge* dst, unsigned char* probability, int width, int height)
	{
		int w256 = width * 256;
		int cnt = 0;
		int sum = 0;
		int maxSum = 0;
		int left = -1;
		int right = -1;
		int v;
		int state;
		int lastState = -1;

		PossiblePathEdge* posibleEdges = (PossiblePathEdge*)malloc(sizeof(PossiblePathEdge) * width);
		PossiblePathEdge* pe;
		PossiblePathEdge* pe2;

		int cntEdges;
		int sl, sr;


		for (int y = 0; y < height; y++)
		{
			cntEdges = 0;
			left = -1;
			right = -1;

			pe = posibleEdges;

			v = *probability++;
			sum = 128 - v;
			lastState = v >= 128;

			for (int x = 1; x < width; x++)
			{
				v = *probability++;
				state = v >= 128;
				sum += v;
				if (state != lastState)
				{
					pe->Rising = state;
					pe->X = x;
					pe->Sum = 256 * x - 2 * sum;
					pe++;
					cntEdges++;
					lastState = state;
				}
			}

			dst->Y = y;
			dst->Left = -1;
			dst->Right = -1;
			if (sum > w256 - sum)
			{
				maxSum = sum;
				state = 1;
			}
			else
			{
				maxSum = w256 - sum;
				state = 0;
			}

			pe = posibleEdges;
			for (int i = 0; i < cntEdges; i++, pe++)
			{
				if (pe->Rising)
				{
					sl = pe->Sum + sum;
					if (sl > maxSum)
					{
						maxSum = sl;
						dst->Left = pe->X;
						dst->Right = -1;
						state = 1;
					}
					sr = pe->Sum + w256 - sum;
					for (int j = i + 1; j < cntEdges; j += 2)
					{
						pe2 = &posibleEdges[j];
						sl = sr - pe2->Sum;
						if (sl > maxSum)
						{
							maxSum = sl;
							dst->Left = pe->X;
							dst->Right = pe2->X;
							state = 1;
						}
					}
				}
				else
				{
					sl = -pe->Sum + w256 - sum;
					if (sl > maxSum)
					{
						maxSum = sl;
						dst->Left = -1;
						dst->Right = pe->X;
						state = 1;
					}
				}
			}
			if (state)
			{
				dst++;
				cnt++;
			}

		}
		free((void*)posibleEdges);
		return cnt;
	}


	EXPORT_API void DepthTransform(ComputeInfo* ci, Point2D* transform, float* rotate, short* dist, int len)
	{
		int l1 = len;

		Point4D* wordPoints = &(ci->WordPoints[0]);
		int l = DepthTransformImpl(wordPoints, transform, rotate, dist, len);
		ci->WordPointsCount = l;
	}

	// reverzuje pole Int16, ze zdroje src kopiruje do dst
	EXPORT_API void ReverseInt16(short* dst, short* src, int len)
	{
		if ((len % 8) == 0)
		{
			ReverseInt16_8Impl(dst, src, len);
			return;
		}

		dst += len - 1;
		while (len > 0)
		{
			*dst = *src;
			dst--;
			src++;
			len--;
		}
	}

	// kopiruje pole char, ze zdroje src kopiruje do dst
	EXPORT_API void Copy(char* dst, char* src, int len)
	{
		if ((len % 16) == 0)
		{
			Copy_16Impl(dst, src, len);
			return;
		}

		while (len > 0)
		{
			*dst = *src;
			dst++;
			src++;
			len--;
		}
	}

	// kopiruje pole RGB do pole BGR32
	EXPORT_API void CopyBGR24ToBGR32(BGR32* dst, BGR* src, int len)
	{
		if ((len % 4) == 0 && len > 4)
		{
			CopyBGR24ToBGR32Impl(dst, src, len - 4);
			//podleni 4 pixely se musi udelat pixel po pixelu, duvodem je ze v ASM se nacita najednou 16 bajtu, to je vic jak 4 pixely a dostavam se mimo pole a muze vznikat vyjimka
			dst = &dst[len - 4];
			src = &src[len - 4];
			len = 4;
		}

		while (len > 0)
		{
			BGR r = src[0];
			BGR32 b;
			b.B = r.B;
			b.R = r.R;
			b.G = r.G;
			b.A = 0;
			*dst = b;
			dst++;
			src++;
			len--;
		}
	}


	// kopiruje pole RGB do pole BGR32
	EXPORT_API void CopyRGB24ToBGR32(BGR32* dst, RGB* src, int len)
	{
		if ((len % 4) == 0)
		{
			CopyRGB24ToBGR32Impl(dst, src, len - 4);
			//podleni 4 pixely se musi udelat pixel po pixelu, duvodem je ze v ASM se nacita najednou 16 bajtu, to je vic jak 4 pixely a dostavam se mimo pole a muze vznikat vyjimka
			dst = &dst[len - 4];
			src = &src[len - 4];
			len = 4;
		}

		while (len > 0)
		{
			RGB r = src[0];
			BGR32 b;
			b.B = r.B;
			b.R = r.R;
			b.G = r.G;
			b.A = 0;
			*dst = b;
			dst++;
			src++;
			len--;
		}
	}

	// kopiruje pole RGB do pole BGR32 v reverznim poradi
	EXPORT_API void ReverseRGB24ToBGR32(BGR32* dst, RGB* src, int len)
	{
		if ((len % 4) == 0)
		{
			ReverseRGB24ToBGR32Impl(dst, src, len);
			return;
		}

		src += len - 1;
		while (len > 0)
		{
			RGB r = src[0];
			BGR32 b;
			b.B = r.B;
			b.R = r.R;
			b.G = r.G;
			b.A = 0;
			*dst = b;
			dst++;
			src--;
			len--;
		}
	}


	EXPORT_API void Test1()
	{
		AggregateItem ai[3];
		int ua[3];
		Point4D points[3];

		ai[0].Count = 100;
		ai[0].SumX = 100;
		ai[0].SumY = 200;
		ai[0].SumZ = 300;
		ai[0].SumZ2 = 960;

		ai[2].Count = 10;
		ai[2].SumX = 100;
		ai[2].SumY = 200;
		ai[2].SumZ = 300;
		ai[2].SumZ2 = 860;

		ua[0] = 0;
		ua[1] = 2;

		int b = ExtractObstaclesImpl(ai, ua, 2, points, 15, 0.0025);
	}


	EXPORT_API void Test2()
	{
		Point4D dst[5];
		short dist[5];
		float r[16];
		Point2D transform[5];

		transform[0].x = -1;
		transform[0].y = -2;
		transform[1].x = -1;
		transform[1].y = -3;
		transform[2].x = -1;
		transform[2].y = -4;
		transform[3].x = 1;
		transform[3].y = 5;
		transform[4].x = 1;
		transform[4].y = 6;

		dist[0] = 0;
		dist[1] = -1;
		dist[2] = 1000;
		dist[3] = 2000;
		dist[4] = 3000;

		for (int i = 0; i < 16; i++)
			r[i] = 0;
		r[0] = 1;
		r[5] = 1;
		r[10] = 1;
		r[15] = 1;

		int cnt = DepthTransformImpl(dst, transform, r, dist, 5);
		/*
				Assert.AreEqual(3, cnt, "Count");

				AreEqual(new Point4D(){ X = 3, Y = 18, Z = 3, A = 1 }, dst[0], "dst[0]");
				AreEqual(new Point4D(){ X = 2, Y = 10, Z = 2, A = 1 }, dst[1], "dst[1]");
				AreEqual(new Point4D(){ X = -1, Y = -4, Z = 1, A = 1 }, dst[2], "dst[2]");
				*/



				/*
				AggregateItem* ais = (AggregateItem*)Alloc(sizeof(AggregateItem) * 15);

				InitAggregate(ais, 15);

				int* uais = (int*)Alloc(sizeof(int) * 15);;;
				Point4D v;
				v.x = 0;
				v.y = 0;
				v.z = 1;
				v.a = 0;

				Point4D* points = (Point4D*)Alloc(sizeof(Point4D) * 7);;

				points[0].x = 4;
				points[0].y = 0;
				points[0].z = 1;
				points[0].a = 1;

				points[1].x = 20;
				points[1].y = 0;
				points[1].z = 1;
				points[1].a = 1;

				points[2].x = -20;
				points[2].y = 0;
				points[2].z = 1;
				points[2].a = 1;

				points[3].x = 0;
				points[3].y = 2;
				points[3].z = 1;
				points[3].a = 1;

				points[4].x = 0;
				points[4].y = 1.9f;
				points[4].z = 2;
				points[4].a = 1;

				points[5].x = 0;
				points[5].y = 20;
				points[5].z = 1;
				points[5].a = 1;

				points[6].x = 0;
				points[6].y = -20;
				points[6].z = 1.1;
				points[6].a = 1;

				int cnt = AggregateObstaclesImpl(points, 7, 2, 2, 1, ais, uais, 5, 3, v);
				*/
	}

	void TestXYZ2Plane()
	{
		ResetPlaneParams(&modelParams);

		points[0].x = 1;
		points[0].y = 0;
		points[0].z = 0;
		points[0].a = 1;
		points[1].x = 2;
		points[1].y = 0;
		points[1].z = 0;
		points[1].a = 1;
		points[2].x = 0;
		points[2].y = 10;
		points[2].z = -1;
		points[2].a = 1;
		points[3].x = 10;
		points[3].y = 10;
		points[3].z = 1;
		points[3].a = 1;
		points[4].x = 10;
		points[4].y = 10;
		points[4].z = 1000;
		points[4].a = 1;


		XYZ2PlaneImpl(&modelParams, points, 2, 5);

		printf("XYZ2Plane:\r\n");
		if (modelParams.SumX != 13)
			printf("Chyba: modelParams.SumX=%f, ocekavano 13.\r\n", modelParams.SumX);
		if (modelParams.SumY != 20)
			printf("Chyba: modelParams.SumY=%f, ocekavano 20.\r\n", modelParams.SumY);
		if (modelParams.SumZ != 0)
			printf("Chyba: modelParams.SumZ=%f, ocekavano 20.\r\n", modelParams.SumZ);
		if (modelParams.Count1 != 4)
			printf("Chyba: modelParams.Count1=%f, ocekavano 4.\r\n", modelParams.Count1);

		if (modelParams.SumXY != 100)
			printf("Chyba: modelParams.SumXY=%f, ocekavano 100.\r\n", modelParams.SumXY);
		if (modelParams.SumYZ != 0)
			printf("Chyba: modelParams.SumYZ=%f, ocekavano 0.\r\n", modelParams.SumYZ);
		if (modelParams.SumZX != 10)
			printf("Chyba: modelParams.SumZX=%f, ocekavano 10.\r\n", modelParams.SumZX);
		if (modelParams.Count1 != 4)
			printf("Chyba: modelParams.Count1=%f, ocekavano 4.\r\n", modelParams.Count1);

		if (modelParams.SumXX != 105)
			printf("Chyba: modelParams.SumXX=%f, ocekavano 105.\r\n", modelParams.SumXX);
		if (modelParams.SumYY != 200)
			printf("Chyba: modelParams.SumYY=%f, ocekavano 200.\r\n", modelParams.SumYY);
		if (modelParams.SumZZ != 2)
			printf("Chyba: modelParams.SumZZ=%f, ocekavano 2.\r\n", modelParams.SumZZ);
		if (modelParams.Count1 != 4)
			printf("Chyba: modelParams.Count1=%f, ocekavano 4.\r\n", modelParams.Count1);


	}


}
