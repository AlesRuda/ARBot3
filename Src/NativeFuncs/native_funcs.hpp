#ifndef native_func_HPP
#define native_func_HPP

// Definice makra pro export podle platformy
#if defined(_WIN32)
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __attribute__((visibility("default")))
#endif


struct PathEdge
{
	int Left, Right, Y;
};

struct PossiblePathEdge
{
	short Rising, X;
	int Sum;
};

struct RGB
{
	char R, G, B;
};

struct BGR
{
	char B, G, R;
};

struct BGR32
{
	char B, G, R, A;
};

struct Point4D
{
	float x, y, z;
	float a = 1;
};

struct PlaneParams
{
	float SumX, SumY, SumZ, Count1;
	float SumXY, SumYZ, SumZX, Count2;
	float SumXX, SumYY, SumZZ, Count3;
	Point4D v; //z'=v.p=v.x*p.x+v.y*p.y+v.z*p.z+v.a*p.a, pokud v.x=a, v.y=b, v.z=0, v.a=c, dostavam rovnici roviny z=a*x+b*y+c
};


struct Point2D
{
	float x, y;
};

struct Point
{
	int x, y;
};

struct AggregateItem
{
	float SumX, SumY, SumZ, Count, SumZ2;
	float pad1, pad2, pad3;
	//	float SumX, SumY, SumZ, SumZ2, Count;
		//	float Sum3, Sum4, Min, Max;
};

struct ComputeInfo
{
	//parametry prolozene roviny levou kamerou 
	PlaneParams LeftCameraParams;
	//parametry prolozene roviny pravou kamerou 
	PlaneParams RightCameraParams;
	// maximalni delka pole CameraPoints
	int MaxCameraPoints;
	//xyz body z kamery (x - doleva, y - roste smerem dolu a z roste smerem od kemery)
	Point4D* CameraPoints;
	// pocet bodu v poli CameraPoints
	int CameraPointsCount;
	//xyz body z hloubkoveho obrazku ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
	Point4D* WordPoints;
	// pocet bodu v poli WordPoints
	int WordPointsCount;
	//body prekazek  - xyz body v orientaci kamery tj. podle left/right TransformMatrix - x roste na vychod, y roste na sever a z smerem nahoru
	Point4D* ObstaclePoints;
	// pocet bodu v poli ObstaclePoints
	int ObstaclePointsCount;
	//body prekazek  - xyz body ve svetove orientaci - x roste na vychod, y roste na sever a z smerem nahoru
	Point4D* WordObstaclePoints;
	// pocet bodu v poli WordObstaclePoints
	int WordObstaclePointsCount;
	//Sirka agregacniho pele
	int Width;
	//Vyska agregacniho pele
	int Height;
	// posunuti v agregacnim poli v ose x
	int xOff;
	// posunuti v agregacnim poli v ose y
	int yOff;
	// rozliseni agregacniho pole
	float Resolution;
	// agregacni pole o velikosti Width*Height
	AggregateItem* Aggregates;
	// pocet bodu v poli Aggregates
	int AggregatesCount;
	// pocet pouzitych agregacnich prvku
	int UsedAggregatesCount;
	//pole odkazu pouzitych agregacnich itemu
	int* UsedAggregates;
};

extern "C"
{
	EXPORT_API void TestCopy(void* ptr1, void* ptr2, int mode, int cnt);

	//Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
	EXPORT_API void BackProject(char* probability, BGR* img, char* backProjectTab, int len);

	//Alokuje potrebne struktury pro vypocet 3D segmentace 
	EXPORT_API ComputeInfo* ComputeAlloc(int maxPoints, int width, int height, int xOff, int yOff, float resolution);
	//uvolnuje naalokovane zdroje metodou ComputeAlloc
	EXPORT_API void ComputeFree(ComputeInfo* ci);

	EXPORT_API void Segment(ComputeInfo* ci, PlaneParams* params, short* dist,
		float* transformMatrix, Point2D* transform, int len, float maxZ);

	EXPORT_API void Segment2(ComputeInfo* ci,
		short* leftDist, float* leftTransformMatrix, Point2D* leftTransform,
		short* rightDist, float* rightTransformMatrix, Point2D* rightTransform,
		float* globalTransformMatrix,
		int len, float maxZ);

	EXPORT_API int FindPathEdge_old(PathEdge* dst, unsigned char* probability, int width, int height);

	EXPORT_API int FindPathEdge(PathEdge* dst, unsigned char* probability, int width, int height);

	EXPORT_API void DepthTransform(ComputeInfo* ci, Point2D* transform, float* rotate, short* dist, int len);

	EXPORT_API void Test1();
	EXPORT_API void Test2();


	// Alokuje blok pameti
	// len - delka bloku v bajtech
	EXPORT_API void* Alloc(int len);
	// Uvolnuje blok pameti
	// ptr - pointer na blok pameti, ktery ma byt ovolnen, puvodne vracena hodnota metodou Alloc
	EXPORT_API void Free(void* ptr);




	// resetuje agrgovane udaje ve vypoctu aproximace bodu rovinou
	EXPORT_API void ResetPlaneParams(PlaneParams* p);

	// prepocte agregovane hodnoty pro prolozeni bodu rovinou na rovinu
	// vysledkem bude nastaveni atributu v
	EXPORT_API void CalcPlaneParams(PlaneParams* p);

	//Inizializuje pouzite agregacni itemy nastavenim Count na 0
	EXPORT_API void ClearAggregateImpl(AggregateItem* ais, int* uais, int cnt);

	//extrahuje z agregacniho pole prekazky
	//ais - pole agregacnich bodu o velikosti width*height
	//uais - pole offsetu na pouzite agregacni body o velikosti width*height
	//len - pocet zaznamu v uais
	//ops - vracene pole prekazek
	//minCount - minimalni pocet agregovanych bodu v jednom AggregateItem, aby mohl byt povazovan za prekazku
	//minStd2 - minimum kavdratu rozptylu v jednom AggregateItem, aby mohl byt povazovan za prekazku
	EXPORT_API int ExtractObstaclesImpl(AggregateItem* ais, int* uais, int len, Point4D* ops, float minCount, float minStd2);

	//Agreguje body sveta v rovine x,y pro budouci extrakci prekazek. 
	//wordPoints - pole bodu sveta v metrech
	//wordPointsCount - pocet bodu sveta
	//r - rozliseni pro agregaci
	//xOff, yOff - posunuti v agregacnim poli ais
	//ais - pole agregacnich bodu o velikosti width*height
	//uais - pole offsetu na pouzite agregacni body o velikosti width*height
	//width, height - sirka a viska agregacniho pole, sirka odpovida souradnici x
	//v - rovnice roviny po ktere robot jede, vznika regresi z bodu v okoli robotu, slouzi pro upravu z souradnice agregovaneho bodu z' = v.x * p.x + v.y * p.y + v.z * p.z + v.a * p.a; 
	//vraci pocet obsazenych agregacnich bodu
	EXPORT_API int AggregateObstacles(Point4D* wordPoints, int wordPointsCount, float r, int xOff, int yOff, AggregateItem* ais, int* uais, int width, int height, Point4D v);
	EXPORT_API int AggregateObstaclesImpl(Point4D* wordPoints, int wordPointsCount, float r, int xOff, int yOff, AggregateItem* ais, int* uais, int width, int height, Point4D v);

	// z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery(x - roste doprava, y - roste dolu a z od kamery)
	// nasledne bod pootoci v prostoru pomoci rotate
	// transform je pole vektoru xy, plati xyz = [x*dist, y*dist, dist], pole transform a dist obsahuje len prvku,
	// hodnoty 0 a -1 v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
	// funkce vraci pocet zapsanych zaznamu do dst
	EXPORT_API int DepthTransformImpl(Point4D* dst, Point2D* transform, float* rotate, short* dist, int len);

	// z hloubkoveho obrazu dist vypocte xyz souradnice bodu v prostoru kamery(x - roste doprava, y - roste dolu a z od kamery)
	// nasledne bod pootoci v prostoru pomoci rotate
	// transform je pole vektoru xy, plati dst[i] = [transform[i].x * dist[i], transform[i].y * dist[i], dist[i]] * rotate, pole transform, dst a dist obsahuje len prvku,
	// nektere hodnoty v dist reprezentuji nezmerenou hodnotu, tyto body se do vystupu dst ulozi jako[0, 0, 0, 0]
	// data se do dst ukladaji v opacnem poradi oproti dist
	EXPORT_API int DepthTransform2Impl(Point4D* dst, Point2D* transform, float* rotate, short* dist, int len);

	//pole vektoru Point4D vynasobi matici transform a vysledek ulozi do dst
	//delka vstupniho a vystupniho pole je len
	//dst=transform*src
	EXPORT_API void TransformPoint4DImpl(Point4D* dst, float* transform, Point4D* src, int len);

	//z hloubkoveho obrazu vypocte xyz souradnice bodu v prostoru kamery (x - roste dopprava, y - roste dolu a z od kamery)
	//transform je pole vektoru xy, plati xyz=[x*dist, y*dist, dist], pole transform a dist obsahuje len prvku,
	//hodnoty 0 a -1 v dist reporezentuji nezmerenou hodnotu, tyto body se do vystupu dst neukladaji
	//hodnoty do dst se ukladaji od oknce dist
	//funkce vraci pocet zapsanych zaznamu do dst
	EXPORT_API int Depth2XYZImpl(Point4D* dst, short* dist, Point2D* transform, int len);

	//Prolozi rovinou mnozinu bodu jejichz abs(z) je meni jak MaxZ.
	//z=a*x+b*y+d
	//Lepe receno spocte parametry pro vypocet prolozeni
	EXPORT_API void XYZ2PlaneImpl(PlaneParams* param, Point4D* src, float maxZ, int len);

	//Pro kazdy pixel BGR vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
	EXPORT_API void BackProjectImpl(char* probability, BGR* img, char* backProjectTab, int len);
	//Pro kazdy pixel BGR32 vezme nejvysi 4 bity barvy, slozi index do backProjectTab a vysledek ulozi do probability
	EXPORT_API void BackProjectBGR32Impl(char* probability, BGR* img, char* backProjectTab, int len);

	// reverzuje pole Int16, ze zdroje src kopiruje do dst
	EXPORT_API void ReverseInt16(short* dst, short* src, int len);
	// reverzuje pole Int16, ze zdroje src kopiruje do dst,
	// len musi byt v nasobcich 16
	EXPORT_API void ReverseInt16_8Impl(short* dst, short* src, int len);


	// kopiruje pole char, ze zdroje src kopiruje do dst
	EXPORT_API void Copy(char* dst, char* src, int len);

	// kopiruje pole char, ze zdroje src kopiruje do dst
	// delka vstupniho a vystupniho pole je len a musi byt v nasobcich 16
	EXPORT_API void Copy_16Impl(char* dst, char* src, int len);

	// kopiruje pole BGR do pole BGR32
	EXPORT_API void CopyBGR24ToBGR32(BGR32* dst, BGR* src, int len);
	EXPORT_API void CopyBGR24ToBGR32Impl(BGR32* dst, BGR* src, int len);


	// kopiruje pole RGB do pole BGR32
	EXPORT_API void CopyRGB24ToBGR32(BGR32* dst, RGB* src, int len);
	EXPORT_API void CopyRGB24ToBGR32Impl(BGR32* dst, RGB* src, int len);

	// kopiruje pole RGB do pole BGR32 v reverznim poradi
	EXPORT_API void ReverseRGB24ToBGR32(BGR32* dst, RGB* src, int len);
	EXPORT_API void ReverseRGB24ToBGR32Impl(BGR32* dst, RGB* src, int len);

}
#endif
