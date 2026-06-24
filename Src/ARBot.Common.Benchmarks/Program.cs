using System.Diagnostics;
using CustomMatrix = ARBot.Common.Common.Matrix;
using MathNet.Numerics.LinearAlgebra;
using MNMatrix = MathNet.Numerics.LinearAlgebra.Matrix<double>;

// Rozmery odpovidaji realnemu EKF v projektu:
//   stav (state)       = 6
//   mereni (measurement) = 13
// Hot-path EKF.Update obsahuje inverzi (C*P*Ct + R) -> matice 13x13.
const int STATE = 6;
const int MEAS = 13;

// ---- Deterministicka testovaci data ------------------------------------
// Linearni kongruentni generator, at je beh reprodukovatelny bez Random.
static double[,] BuildSpd(int n, ref ulong seed)
{
    // A = M*M^T + n*I  -> symetricka pozitivne definitni (jiste invertovatelna)
    var m = new double[n, n];
    for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            m[i, j] = ((seed >> 33) / (double)(1UL << 31)) - 1.0;
        }
    var spd = new double[n, n];
    for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
        {
            double s = 0;
            for (int k = 0; k < n; k++) s += m[i, k] * m[j, k];
            spd[i, j] = s + (i == j ? n : 0);
        }
    return spd;
}

static double[,] BuildDense(int rows, int cols, ref ulong seed)
{
    var m = new double[rows, cols];
    for (int i = 0; i < rows; i++)
        for (int j = 0; j < cols; j++)
        {
            seed = seed * 6364136223846793005UL + 1442695040888963407UL;
            m[i, j] = ((seed >> 33) / (double)(1UL << 31)) - 1.0;
        }
    return m;
}

ulong seed = 0x1234_5678_9ABC_DEF0UL;
double[,] pData = BuildSpd(STATE, ref seed);   // 6x6 kovariance
double[,] rData = BuildSpd(MEAS, ref seed);    // 13x13 sum mereni
double[,] cData = BuildDense(MEAS, STATE, ref seed); // 13x6 linearizace
double[,] mData = BuildSpd(STATE, ref seed);   // 6x6 prechodova matice

// ---- Jedna iterace EKF kroku: vlastni Matrix ----------------------------
static double EkfStepCustom(double[,] pData, double[,] rData, double[,] cData, double[,] mData)
{
    var P = new CustomMatrix(pData);
    var R = new CustomMatrix(rData);
    var C = new CustomMatrix(cData);
    var M = new CustomMatrix(mData);

    var Ct = CustomMatrix.Transpose(C);                 // 6x13
    var innov = C * P * Ct + R;                         // 13x13
    var K = P * Ct * CustomMatrix.Inverse(innov);       // 6x13
    var correctedP = P - K * C * P;                     // 6x6
    var predictedP = M * correctedP * CustomMatrix.Transpose(M); // 6x6
    return predictedP[0, 0];
}

// ---- Jedna iterace EKF kroku: MathNet -----------------------------------
static double EkfStepMathNet(MNMatrix P, MNMatrix R, MNMatrix C, MNMatrix M)
{
    var Ct = C.Transpose();                             // 6x13
    var innov = C * P * Ct + R;                         // 13x13
    var K = P * Ct * innov.Inverse();                   // 6x13
    var correctedP = P - K * C * P;                     // 6x6
    var predictedP = M * correctedP * M.Transpose();    // 6x6
    return predictedP[0, 0];
}

// ---- Izolovana inverze 13x13 --------------------------------------------
static double InvCustom(double[,] data) => CustomMatrix.Inverse(new CustomMatrix(data))[0, 0];
static double InvMathNet(MNMatrix m) => m.Inverse()[0, 0];

// ---- Harness ------------------------------------------------------------
static void Bench(string name, int warmup, int iters, Func<double> body)
{
    double acc = 0;
    for (int i = 0; i < warmup; i++) acc += body();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long allocStart = GC.GetTotalAllocatedBytes(true);
    int gc0Start = GC.CollectionCount(0);

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < iters; i++) acc += body();
    sw.Stop();

    long allocEnd = GC.GetTotalAllocatedBytes(true);
    int gc0End = GC.CollectionCount(0);

    double nsPerOp = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / iters;
    double bytesPerOp = (allocEnd - allocStart) / (double)iters;
    Console.WriteLine($"{name,-34} {nsPerOp,12:N0} ns  {bytesPerOp,10:N0} B/op  gc0={gc0End - gc0Start,4}  (sink={acc:E2})");
}

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"Process: {(Environment.Is64BitProcess ? "x64" : "x86")}, Server GC: {System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine($"Dims: state={STATE}, meas={MEAS}\n");

const int WARMUP = 5_000;
const int ITERS = 100_000;

// Predpripravene MathNet matice (znovupouzite napric iteracemi,
// stejne jako vlastni Matrix vychazi z hotovych double[,]).
var pMN = MNMatrix.Build.DenseOfArray(pData);
var rMN = MNMatrix.Build.DenseOfArray(rData);
var cMN = MNMatrix.Build.DenseOfArray(cData);
var mMN = MNMatrix.Build.DenseOfArray(mData);

Console.WriteLine("== Cely EKF krok (transpose + 2 mult + inverze 13x13 + update) ==");
Bench("EKF step  | custom Matrix", WARMUP, ITERS, () => EkfStepCustom(pData, rData, cData, mData));
Bench("EKF step  | MathNet",       WARMUP, ITERS, () => EkfStepMathNet(pMN, rMN, cMN, mMN));

Console.WriteLine("\n== Izolovana inverze 13x13 ==");
Bench("Inverse13 | custom Matrix", WARMUP, ITERS, () => InvCustom(rData));
Bench("Inverse13 | MathNet",       WARMUP, ITERS, () => InvMathNet(rMN));
