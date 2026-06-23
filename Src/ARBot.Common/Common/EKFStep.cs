using ARBot.Common.Logs;
using ARBot.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Jeden krok rozsireneho kalmanova filtru
    /// </remarks>
    /// <typeparam name="TState">Vektor stav - x</typeparam>
    /// <typeparam name="TMeasurement">Vektor mereni/vystupu - y</typeparam>
    /// <typeparam name="TInput">Vektor vstupu - u</typeparam>
    public class EKFStep<TState, TMeasurement, TInput> : IEKFStepInfo where TState : Matrix where TMeasurement : Matrix where TInput : Matrix
    {
        public EKF<TState, TMeasurement, TInput> Parent { get; private set; }
        public int Index { get; set; }


        #region IEKFStepInfo
        Matrix IEKFStepInfo.C => C;

        Matrix IEKFStepInfo.CorrectedP => CorrectedP;

        Matrix IEKFStepInfo.CurrentState => CurrentState;

        Matrix IEKFStepInfo.Diff => Diff;

        Matrix IEKFStepInfo.FilteredOutput => FilteredOutput;

        Matrix IEKFStepInfo.Input => Input;

        Matrix IEKFStepInfo.K => K;

        Matrix IEKFStepInfo.M => M;

        Matrix IEKFStepInfo.Output => Output;

        Matrix IEKFStepInfo.PredictedP => PredictedP;

        Matrix IEKFStepInfo.PredictedState => PredictedState;

        Matrix IEKFStepInfo.PrevP => PrevP;

        Matrix IEKFStepInfo.PrevState => PrevState;

        Matrix IEKFStepInfo.Q => Q;

        Matrix IEKFStepInfo.R => R;

        Matrix IEKFStepInfo.R_Internal => R_Internal;

        #endregion

        /// <summary>
        /// Predchozi kovariance stavu
        /// </summary>
        public Matrix PrevP;
        /// <summary>
        /// Predikovana kovariance stavu
        /// </summary>
        public Matrix PredictedP;
        /// <summary>
        /// Korigovana kovariance stavu
        /// </summary>
        public Matrix CorrectedP;
        /// <summary>
        /// Linearizovana matice systemu 
        /// x(k+1)=M*x(k)+N*u(k)
        /// realne se pouziva nelinearni vypocet PredictState
        /// M=LinearizeM(x, u) - parcialni derivace PredictState podle jednotlivych slozek x
        /// </summary>
        public Matrix M;
        /// <summary>
        /// Linearizovana matice mereni/pozorovani
        /// y(k+1)=C*x(k)+D*u(k)
        /// realne se pouziva nelinearni vypocet CalcOutput
        /// C=LinearizeC(x, u) - parcialni derivace CalcOutput podle jednotlivych slozek x
        /// </summary>
        public Matrix C;
        /// <summary>
        /// Matice sumu systemu
        /// </summary>
        public Matrix Q;
        /// <summary>
        /// Matice sumu mereni
        /// </summary>
        public Matrix R;
        public Matrix R_Internal;
        /// <summary>
        /// Kalmanovo zesileni
        /// </summary>
        public Matrix K;
        /// <summary>
        /// Odchylka odhadu mereni
        /// </summary>
        public Matrix Diff;
        /// <summary>
        /// x(k) - odhad aktualni stavu
        /// </summary>
        public TState PrevState;
        /// <summary>
        /// x'(k) - hodnota stavu po filtracnim kroku
        /// </summary>
        public TState CurrentState;
        /// <summary>
        /// x(k+1) - odhad budouciho stavu
        /// </summary>
        public TState PredictedState;

        /// <summary>
        /// y'(k) - hodnota vystypu po filtracnim kroku
        /// </summary>
        public TMeasurement FilteredOutput;

        /// <summary>
        /// Merena hodnota vystupu
        /// </summary>
        public TMeasurement Output;
        /// <summary>
        /// Vstupni vektor
        /// </summary>
        public TInput Input;


        /// <summary>
        /// Vyznam jednotlivych prvku vektoru mereni
        /// </summary>
        public string[] MeasurementDescriptions => Parent?.MeasurementDescriptions;
        /// <summary>
        /// Vyznam jednotlivych prvku stavoveho vektoru
        /// </summary>
        public string[] StateDescriptions => Parent?.StateDescriptions;
        /// <summary>
        /// Vyznam jednotlivych prvku vstupniho vektoru
        /// </summary>
        public string[] InputDescriptions => Parent?.InputDescriptions;


        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <remarks>
        /// Stavy je nutne vytvorit v potomkovi.
        /// CurrentState = new TState();
        /// PredictedState = new TState();
        /// CurrentOutput = new TMeasurement();
        /// </remarks>
        public EKFStep(EKF<TState, TMeasurement, TInput> parent)
        {
            Parent = parent;
        }

        public EKFStepMsg ToLogMessage()
        {
            return new EKFStepMsg()
            {
                C = C,
                CorrectedP = CorrectedP,
                CurrentState = CurrentState,
                Diff = Diff,
                FilteredOutput = FilteredOutput,
                Input = Input,
                K = K,
                M = M,
                Output = Output,
                PredictedP = PredictedP,
                PredictedState = PredictedState,
                PrevP = PrevP,
                PrevState = PrevState,
                Q = Q,
                R = R,
                R_Internal = R_Internal,
                InputDescriptions = InputDescriptions,
                MeasurementDescriptions = MeasurementDescriptions,
                StateDescriptions = StateDescriptions
            };
        }
    }
}
