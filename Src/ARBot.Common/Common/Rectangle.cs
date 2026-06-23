using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Ctverec
    /// </summary>
    public class Rectangle
    {
        public Rectangle()
        {
        }
        public Rectangle(double x1, double y1, double x2, double y2)
        {
            X = x1;
            Y = y1;
            Width = x2 - x1;
            Height = y2 - y1;
        }
        public double X, Y, Width, Height;
        public Point2D LeftTop => new Point2D(X, Y + Height);
        public Point2D RightTop => new Point2D(X + Width, Y + Height);
        public Point2D LeftBottom => new Point2D(X, Y); 
        public Point2D RightBottom => new Point2D(X + Width, Y); 

        public Rectangle Offset(double x, double y)
        {
            return new Rectangle(X - x, Y - y, X + Width + x, Y + Height + y);
        }
    }
}
