using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;
using ARBot.Common.Models;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Logs
{
    public class EKFStepMsg : Message, IEKFStepInfo
    {
        /// <summary>
        /// Konstruktor
        /// </summary>
        public EKFStepMsg() : base("EKFStep", 1)
        {
        }


        /// <summary>
        /// Poradi vypocetniho kroku
        /// </summary>
        public int Index { get; set; }
        /// <summary>
        /// Predchozi kovariance stavu
        /// </summary>
        public Matrix<double>PrevP { get; set; }
        /// <summary>
        /// Predikovana kovariance stavu
        /// </summary>
        public Matrix<double>PredictedP { get; set; }
        /// <summary>
        /// Korigovana kovariance stavu
        /// </summary>
        public Matrix<double>CorrectedP { get; set; }
        /// <summary>
        /// Linearizovana matice systemu 
        /// x(k+1)=M*x(k)+N*u(k)
        /// realne se pouziva nelinearni vypocet PredictState
        /// M=LinearizeM(x, u) - parcialni derivace PredictState podle jednotlivych slozek x
        /// </summary>
        public Matrix<double>M { get; set; }
        /// <summary>
        /// Linearizovana matice mereni/pozorovani
        /// y(k+1)=C*x(k)+D*u(k)
        /// realne se pouziva nelinearni vypocet CalcOutput
        /// C=LinearizeC(x, u) - parcialni derivace CalcOutput podle jednotlivych slozek x
        /// </summary>
        public Matrix<double>C { get; set; }
        /// <summary>
        /// Matice sumu systemu
        /// </summary>
        public Matrix<double>Q { get; set; }
        /// <summary>
        /// Matice sumu mereni
        /// </summary>
        public Matrix<double>R { get; set; }
        public Matrix<double>R_Internal { get; set; }
        /// <summary>
        /// Kalmanovo zesileni
        /// </summary>
        public Matrix<double>K { get; set; }
        /// <summary>
        /// Odchylka odhadu mereni
        /// </summary>
        public Matrix<double>Diff { get; set; }
        /// <summary>
        /// x(k) - odhad aktualni stavu
        /// </summary>
        public Matrix<double>PrevState { get; set; }
        /// <summary>
        /// x'(k) - hodnota stavu po filtracnim kroku
        /// </summary>
        public Matrix<double>CurrentState { get; set; }
        /// <summary>
        /// x(k+1) - odhad budouciho stavu
        /// </summary>
        public Matrix<double>PredictedState { get; set; }

        /// <summary>
        /// y'(k) - hodnota vystypu po filtracnim kroku
        /// </summary>
        public Matrix<double>FilteredOutput { get; set; }

        /// <summary>
        /// Merena hodnota vystupu
        /// </summary>
        public Matrix<double>Output { get; set; }
        /// <summary>
        /// Vstupni vektor
        /// </summary>
        public Matrix<double>Input { get; set; }


        /// <summary>
        /// Vyznam jednotlivych prvku vektoru mereni
        /// </summary>
        public string[] MeasurementDescriptions { get; set; }
        /// <summary>
        /// Vyznam jednotlivych prvku stavoveho vektoru
        /// </summary>
        public string[] StateDescriptions { get; set; }
        /// <summary>
        /// Vyznam jednotlivych prvku vstupniho vektoru
        /// </summary>
        public string[] InputDescriptions { get; set; }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Index);
            Write(bw, MeasurementDescriptions);
            Write(bw, StateDescriptions);
            Write(bw, InputDescriptions);

            Write(bw, PrevP);
            Write(bw, PredictedP);
            Write(bw, CorrectedP);
            Write(bw, M);
            Write(bw, C);
            Write(bw, Q);
            Write(bw, R);

            Write(bw, R_Internal);
            Write(bw, K);
            Write(bw, Diff);
            Write(bw, PrevState);
            Write(bw, CurrentState);
            Write(bw, PredictedState);

            Write(bw, FilteredOutput);
            Write(bw, Output);
            Write(bw, Input);

        }

        public override void FromData(BinaryReader br)
        {

            Index = br.ReadInt32();
            MeasurementDescriptions = ReadStringArray(br);
            StateDescriptions = ReadStringArray(br);
            InputDescriptions = ReadStringArray(br);

            PrevP = ReadMatrixDouble(br);
            PredictedP = ReadMatrixDouble(br);
            CorrectedP = ReadMatrixDouble(br);
            M = ReadMatrixDouble(br);
            C = ReadMatrixDouble(br);
            Q = ReadMatrixDouble(br);
            R = ReadMatrixDouble(br);

            R_Internal = ReadMatrixDouble(br);
            K = ReadMatrixDouble(br);
            Diff = ReadMatrixDouble(br);
            PrevState = ReadMatrixDouble(br);
            CurrentState = ReadMatrixDouble(br);
            PredictedState = ReadMatrixDouble(br);

            FilteredOutput = ReadMatrixDouble(br);
            Output = ReadMatrixDouble(br);
            Input = ReadMatrixDouble(br);
        }

        public override Message Build()
        {
            return new EKFStepMsg();
        }

        public override string ToString()
        {
            return string.Format("EKFStep");
        }
    }
}
