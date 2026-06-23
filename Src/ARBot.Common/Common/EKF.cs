using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Realizuje rozsireny kalmanuv filtr.
    /// x(k+1)=PredictState(x(k), u(k))
    /// y(k+1)=CalcOutput(x(k+1), u(k))
    /// </summary>
    /// <remarks>
    /// Standardni vypocet modelu v cosove domene
    /// x'(t) = A * x(t) + B * u(t)
    /// y'(t) = C * x(t) + D * u(t)
    /// Standardni vypocet modelu v diskretni verzi
    /// x'(k+1) = M * x(k) + N * u(k)
    /// y'(k+1) = C * x(k) + D * u(k)
    /// 
    /// M=e^ATs
    /// Pro regularni A (tj. det A!=0) lze psat 
    /// N=(e^(ATs)-I)*(A^-1)*B=(A^-1)*(e^(ATs)-I)*B
    /// Pro singularni A (tj. det A==0) je nutne udelat rozvoj a integrovat po clenech
    /// 
    /// Ts je vzorkovaci perioda
    /// </remarks>
    /// <typeparam name="TState">Vektor stav - x</typeparam>
    /// <typeparam name="TMeasurement">Vektor mereni/vystupu - y</typeparam>
    /// <typeparam name="TInput">Vektor vstupu - u</typeparam>
    public abstract class EKF<TState, TMeasurement, TInput> where TState : Matrix where TMeasurement : Matrix where TInput : Matrix
    {
        public EKFStep<TState, TMeasurement, TInput> Step;

        /// <summary>
        /// Vyznam jednotlivych prvku vektoru mereni
        /// </summary>
        public string[] MeasurementDescriptions;
        /// <summary>
        /// Vyznam jednotlivych prvku stavoveho vektoru
        /// </summary>
        public string[] StateDescriptions;
        /// <summary>
        /// Vyznam jednotlivych prvku vstupniho vektoru
        /// </summary>
        public string[] InputDescriptions;

        /// <summary>
        /// Konstanta exponencialniho zapominani R.
        /// R(k+1)=Ar*R(k)+(1-Ar)*....
        /// Ar=1 znamena konstantni R, tj neni ovlivneno merenim
        /// Eventualne je mozne prepsat metodu EstimateR
        /// </summary>
        public double Ar = 0.9;
        /// <summary>
        /// Konstanta exponencialniho zapominani Q.
        /// Q(k+1)=Aq*Q(k)+(1-Aq)*....
        /// Aq=1 znamena konstantni Q, tj neni ovlivneno merenim
        /// Eventualne je mozne prepsat metodu EstimateQ
        /// </summary>
        public double Aq = 0.9;

        /// <summary>
        /// x'(k) - hodnota stavu po filtracnim kroku
        /// </summary>
        //        public TState CurrentState=>Step.CurrentState;
        /// <summary>
        /// x(k+1) - odhad budouciho stavu
        /// </summary>
        //      public TState PredictedState => Step.CurrentState;


        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <remarks>
        /// Stavy je nutne vytvorit v potomkovi.
        /// CurrentState = new TState();
        /// PredictedState = new TState();
        /// CurrentOutput = new TMeasurement();
        /// </remarks>
        public EKF()
        {
            Step = new EKFStep<TState, TMeasurement, TInput>(this);
            Step.PrevState = CreateState();
            Step.CurrentState = CreateState();
            Step.PredictedState = CreateState();
            Step.FilteredOutput = CreateMeasurement();

            Step.PredictedP = new Matrix(Step.PrevState.NoRows, Step.PrevState.NoRows);
            Step.CorrectedP = new Matrix(Step.PrevState.NoRows, Step.PrevState.NoRows);
            Step.M = new Matrix(Step.PrevState.NoRows, Step.PrevState.NoCols);
            Step.C = new Matrix(Step.FilteredOutput.NoRows, Step.PrevState.NoRows);
            Step.Q = new Matrix(Matrix.Identity(Step.PrevState.NoRows));
            Step.R = new Matrix(Matrix.Identity(Step.FilteredOutput.NoRows));
            Step.R_Internal = new Matrix(Matrix.Identity(Step.FilteredOutput.NoRows));
        }

        protected abstract Matrix LinearizeM(TState x, TInput u);
        protected abstract Matrix LinearizeC(TState x, TInput u);

        protected abstract TState PredictState(TState x, TInput u);
        protected abstract TMeasurement CalcOutput(TState x, TInput u);

        /// <summary>
        /// Vytvari stav modelu
        /// </summary>
        /// <returns></returns>
        public abstract TState CreateState();
        /// <summary>
        /// Vytvari vektor mereni
        /// </summary>
        /// <returns></returns>
        public abstract TMeasurement CreateMeasurement();
        /// <summary>
        /// Vytvari vektor vstupu
        /// </summary>
        /// <returns></returns>
        public abstract TInput CreateInput();

        /// <summary>
        /// Vytvori novou matici v niz budou NAN hodnoty nehrazeny hodnotou default.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="defaul"></param>
        /// <returns></returns>
        protected void Set(Matrix m, double defaul)
        {
            for (int i = 0; i < m.NoRows; i++)
            {
                for (int j = 0; j < m.NoCols; j++)
                {
                    m[i, j] = defaul;
                }
            }
        }

        /// <summary>
        /// Vytvori novou matici v niz budou NAN hodnoty nehrazeny hodnotou default.
        /// </summary>
        /// <param name="m"></param>
        /// <param name="defaul"></param>
        /// <returns></returns>
        protected Matrix RemoveNAN(Matrix m, double defaul)
        {
            var r = new Matrix(m.NoRows, m.NoCols);
            for (int i = 0; i < m.NoRows; i++)
            {
                for (int j = 0; j < m.NoCols; j++)
                {
                    r[i, j] = double.IsNaN(m[i, j]) ? defaul : m[i, j];
                }
            }
            return r;
        }

        /// <summary>
        /// Vytvori novu matici jejiz hodnoty jsou nasobeny mul ci nanMul podle hodnot v mask
        /// </summary>
        /// <param name="m">Ctvercova matice</param>
        /// <param name="mask">Sloupcovy vektor, stejneho rozmeru jako m</param>
        /// <param name="mul">nasobici koeficient pro not nan hodnotu v mask</param>
        /// <param name="nanMul">nasobici koeficient pro nan hodnotu v mask</param>
        /// <returns></returns>
        protected Matrix Mask(Matrix m, Matrix mask, double mul, double nanMul)
        {
            var r = new Matrix(m.NoRows, m.NoCols);
            bool isNaN;
            for (int i = 0; i < m.NoRows; i++)
            {
                for (int j = 0; j < m.NoCols; j++)
                {
                    isNaN = double.IsNaN(mask[i, 0]) || double.IsNaN(mask[j, 0]);
                    if (double.IsInfinity(nanMul))
                        r[i, j] = isNaN ? nanMul : m[i, j] * mul;
                    else
                        r[i, j] = m[i, j] * (isNaN ? nanMul : mul);
                }
            }
            return r;
        }

        /// <summary>
        /// Vytvori novu matici jejiz hodnoty podle mask IsNaN budou nahrazeny def
        /// </summary>
        /// <param name="m">Ctvercova matice</param>
        /// <param name="mask">Sloupcovy vektor, stejneho rozmeru jako m</param>
        /// <param name="def">nasobici koeficient pro not nan hodnotu v mask</param>
        /// <returns></returns>
        protected Matrix Mask(Matrix m, Matrix mask, double def)
        {
            var r = new Matrix(m.NoRows, m.NoCols);
            for (int i = 0; i < m.NoRows; i++)
            {
                for (int j = 0; j < m.NoCols; j++)
                {
                    r[i, j] = double.IsNaN(mask[i, 0]) || double.IsNaN(mask[j, 0]) ? def : m[i, j];
                }
            }
            return r;
        }

        protected virtual Matrix Diff(TMeasurement x1, TMeasurement x2)
        {
            return x1 - x2;
        }
        /// <summary>
        /// Odhaduje R z rozdilu merenych hodnot a odhadovane vystupu KF
        /// </summary>
        /// <param name="y"></param>
        /// <param name="u"></param>
        protected void EstimateRExp(TMeasurement y, TInput u)
        {
            double inf = Double.PositiveInfinity;

            var e = Diff(y, Step.FilteredOutput);
            var e1 = RemoveNAN(e, 0);

            Step.R_Internal = Mask(Step.R_Internal, e, Ar, 1) + Mask((e1 * Matrix.Transpose(e1) + Step.C * Step.PrevP * Matrix.Transpose(Step.C)), e, (1 - Ar), 0);
            Step.R = Diag(Mask(Step.R_Internal, e, inf));
        }

        List<TMeasurement> yHist = new List<TMeasurement>();
        /// <summary>
        /// Pocita R z merenych hodnot tj. rozptyl z poslednich cnt vzorku
        /// </summary>
        /// <param name="y"></param>
        /// <param name="u"></param>
        /// <param name="cnt"></param>
        protected void EstimateRAgg(TMeasurement y, TInput u, int cnt)
        {
            yHist.Add(y);
            if (yHist.Count > cnt)
                yHist.RemoveAt(0);

            Matrix sum = new Matrix(y.NoRows, y.NoCols);
            for (int i = 0; i < yHist.Count; i++)
                sum += y;

            TMeasurement avg = CreateMeasurement();
            for (int i = 0; i < y.NoRows; i++)
                avg[i, 0] = sum[i, 0] / yHist.Count;

            double inf = Double.PositiveInfinity;

            var e = Diff(y, avg);
            var e1 = RemoveNAN(e, 0);

            Step.R_Internal = Mask(Step.R_Internal, e, Ar, 1) + Mask((e1 * Matrix.Transpose(e1) + Step.C * Step.PrevP * Matrix.Transpose(Step.C)), e, (1 - Ar), 0);
            Step.R = Diag(Mask(Step.R_Internal, e, inf));
        }

        protected virtual void EstimateR(TMeasurement y, TInput u)
        {
//            EstimateRAgg(y, u, 100);
            EstimateRExp(y, u);
        }

        protected virtual void EstimateQ(TMeasurement y, TInput u)
        {
            var d = RemoveNAN(Diff(y, Step.FilteredOutput), 0);
            Step.Q = Aq * Step.Q + (1 - Aq) * (Step.K * (d * Matrix.Transpose(d)) * Matrix.Transpose(Step.K));
        }


        protected Matrix Diag(Matrix m)
        {
            var n = new Matrix(m.NoRows, m.NoRows);
            for (int i = 0; i < m.NoRows; i++)
                n[i, i] = m[i, i];
            return n;
        }

        public void Update(TMeasurement y, TInput u)
        {
            var oldStep = Step;
            Step = new EKFStep<TState, TMeasurement, TInput>(this);
            Step.Index = oldStep.Index + 1;
            Step.Output = y;
            Step.Input = u;
            Step.FilteredOutput = CreateMeasurement();
            Step.FilteredOutput.in_Mat = oldStep.FilteredOutput.in_Mat.Clone() as double[,];
            Step.PrevState = CreateState();
            Step.PrevState.in_Mat = oldStep.PredictedState.in_Mat.Clone() as double[,];
            Step.CurrentState = CreateState();

            //            Step.K=new Matrix(oldStep.K.in_Mat.Clone() as double[,]);
            Step.R_Internal = new Matrix(oldStep.R_Internal.in_Mat.Clone() as double[,]);
            Step.R = new Matrix(oldStep.R.in_Mat.Clone() as double[,]);
            Step.Q = new Matrix(oldStep.Q.in_Mat.Clone() as double[,]);
            Step.C = new Matrix(oldStep.C.in_Mat.Clone() as double[,]);
            Step.M = new Matrix(oldStep.M.in_Mat.Clone() as double[,]);
            Step.PrevP = new Matrix(oldStep.PredictedP.in_Mat.Clone() as double[,]);
            Step.CorrectedP = new Matrix(oldStep.PredictedP.in_Mat.Clone() as double[,]);

            // *** filtracni krok
            // vypocet N - lienarize modelu
            Step.C = LinearizeC(Step.PrevState, u);
            Matrix Ct = Matrix.Transpose(Step.C);
            //odhad sumu mereni
            EstimateR(y, u);
            //vypocet vystupu
            Step.FilteredOutput = CalcOutput(Step.PrevState, u);
            // Kalmanovo zesileni
            var m = Step.C * Step.PrevP * Ct + Step.R;
            if (Matrix.Det(m) != 0)
                Step.K = Step.PrevP * Ct * Matrix.Inverse(m);
            else
            {
                Step.K = Step.PrevP * Ct * Matrix.Inverse(Diag(m));
            }

            Step.Diff = RemoveNAN(Diff(y, Step.FilteredOutput), 0);
            Step.CurrentState.in_Mat = (Step.PrevState + Step.K * Step.Diff).in_Mat;

            Step.CorrectedP = Step.PrevP - Step.K * Step.C * Step.PrevP;

            // *** predikcni krok
            // vypocet M - linearizace modelu
            Step.M = LinearizeM(Step.CurrentState, u);
            Step.PredictedState = PredictState(Step.CurrentState, u);
            EstimateQ(y, u);

            Step.PredictedP = Step.M * Step.CorrectedP * Matrix.Transpose(Step.M) + Step.Q;
        }
    }
}
