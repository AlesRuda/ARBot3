#if EmguTFLite

using ARBot.Common.Common;
using Emgu.TF;
using Emgu.TF.Lite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class TFSemanticSegmentation : Emgu.TF.Util.UnmanagedObject, IBackProject
    {
        private Interpreter _interpreter = null;
        private FlatBufferModel _model = null;
        private Tensor _inputTensor;
        private Tensor _outputTensor;
        int width, height;

        public TFSemanticSegmentation(String fileName)
        {
            _model = new FlatBufferModel(fileName);

            if (!_model.CheckModelIdentifier())
                throw new Exception("Model identifier check failed");

            _interpreter = new Interpreter(_model);
            _interpreter.SetNumThreads(4);
            Status allocateTensorStatus = _interpreter.AllocateTensors();
            if (allocateTensorStatus == Status.Error)
                throw new Exception("Failed to allocate tensor");

            int[] input = _interpreter.InputIndices;
            _inputTensor = _interpreter.GetTensor(input[0]);

            int[] output = _interpreter.OutputIndices;
            _outputTensor = _interpreter.GetTensor(output[0]);
        }

        private Size GetSize(Tensor t)
        {
            if (t.Dims.Length == 4)
                return new Size(t.Dims[1], t.Dims[2]);
            return new Size(t.Dims[0], t.Dims[1]);
        }

        public void Process(Image<BGR32> srcImg, Image<Gray> destImg)
        {
            var so = GetSize(_outputTensor);

            if (destImg.Width != so.Width)
                throw new ArgumentException("destImg.Width");
            if (destImg.Height != so.Height)
                throw new ArgumentException("destImg.Height");

            var si = GetSize(_inputTensor);

            var sp = new BGR32();
            sp.Data = srcImg.Data;

            double sx = (double)srcImg.Width / (double)si.Width;
            double sy = (double)srcImg.Height / (double)si.Height;

            var vals = new float[si.Width * si.Height*3];

            for (int x = 0; x < si.Width; x++)
            {
                for (int y = 0; y < si.Height; y++)
                {
                    int idx = 3 * (x + y * si.Width);
                    sp.Index = srcImg.Index((int)(x*sx), (int)(y*sy));
                    var c = sp.Color;
                    vals[idx] = ((float)c.B) / 255.0f;
                    vals[idx+1] = ((float)c.G) / 255.0f;
                    vals[idx + 2] = ((float)c.R) / 255.0f;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(vals, 0, _inputTensor.DataPointer, vals.Length);

            _interpreter.Invoke();

            float[] probability = _outputTensor.Data as float[];

            Gray p = new Gray();
            p.Data = destImg.Data;
            for (int x = 0; x < so.Width; x++)
            {
                for (int y = 0; y < so.Height; y++)
                {
                    int idx = 2 * (x + y * so.Width);
                    p.Index = destImg.Index(x, y);
                    p.Value = probability[idx] < probability[1 + idx] ? (byte)255 : (byte)0;
                }
            }
        }

        public Size Size(int width, int height)
        {
            return GetSize(_outputTensor);
        }

        protected override void DisposeObject()
        {
            if (_interpreter != null)
            {
                _interpreter.Dispose();
                _interpreter = null;
            }

            if (_model != null)
            {
                _model.Dispose();
                _model = null;
            }
        }
    }
}
#endif

#if true

using ARBot.Common.Common;
using Emgu.TF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class TFSemanticSegmentation : Emgu.TF.Util.UnmanagedObject, IBackProject
    {
        private Graph _graph = null;
        private Session _session = null;
        Operation input ;
        Operation output ;


        public TFSemanticSegmentation(String fileName, string inputName, string outputName)
        {
            _graph = new Graph();

            string fn = fileName;
            byte[] model = File.ReadAllBytes(fn);

            if (model.Length == 0)
                throw new FileNotFoundException(String.Format("Unable to load file {0}", fn));

            var modelBuffer = Emgu.TF.Buffer.FromString(model);

            Status status = new Status();
            SessionOptions sessionOptions = new SessionOptions();

            using (ImportGraphDefOptions options = new ImportGraphDefOptions())
                _graph.ImportGraphDef(modelBuffer, options, status);

            _session = new Session(_graph, sessionOptions, status);

            input = _graph[inputName];

            output = _graph[outputName];
        }

        private Size GetSize(Tensor t)
        {
            if (t.Dim.Length == 4)
                return new Size(t.Dim[1], t.Dim[2]);
            return new Size(t.Dim[0], t.Dim[1]);
        }

        public void Process(Image<BGR32> srcImg, Image<Gray> destImg)
        {
            var so = Size(0, 0);

            if (destImg.Width != so.Width)
                throw new ArgumentException("destImg.Width");
            if (destImg.Height != so.Height)
                throw new ArgumentException("destImg.Height");

            var si = Size(0, 0);

            var sp = new BGR32();
            sp.Data = srcImg.Data;

            double sx = (double)srcImg.Width / (double)si.Width;
            double sy = (double)srcImg.Height / (double)si.Height;

            var vals = new float[si.Width * si.Height*3];

            int off = si.Width * si.Height;

            for (int x = 0; x < si.Width; x++)
            {
                for (int y = 0; y < si.Height; y++)
                {
                    int idx = 3*(x + y * si.Width);
                    sp.Index = srcImg.Index((int)(x*sx), (int)(y*sy));
                    var c = sp.Color;
                    vals[idx] = ((float)c.R) / 255.0f;
                    vals[idx+ 1] = ((float)c.G) / 255.0f;
                    vals[idx + 2] = ((float)c.B) / 255.0f;
                }
            }

            var t= new Tensor(DataType.Float, new int[] {1, si.Width, si.Height, 3});

            System.Runtime.InteropServices.Marshal.Copy(vals, 0, t.DataPointer, vals.Length);

            Tensor[] finalTensor = _session.Run(new Output[] { input }, new Tensor[] { t }, new Output[] { output });

            float[] probability = finalTensor[0].GetData(false) as float[];


            Gray p = new Gray();
            p.Data = destImg.Data;
            for (int x = 0; x < so.Width; x++)
            {
                for (int y = 0; y < so.Height; y++)
                {
                    int idx = 2 * (x + y * so.Width);
                    p.Index = destImg.Index(x, y);
                    p.Value = probability[idx] < probability[1 + idx] ? (byte)255 : (byte)0;
                }
            }
        }

        public Size Size(int width, int height)
        {
            return new Size(96, 96);
//            return new Size(128, 128);
            //            return GetSize(_outputTensor);
        }

        protected override void DisposeObject()
        {
            if (_graph != null)
            {
                _graph.Dispose();
                _graph = null;
            }

            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }
    }
}

#endif

