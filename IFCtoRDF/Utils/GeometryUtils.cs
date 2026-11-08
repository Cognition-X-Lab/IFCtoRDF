using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IFCtoRDF
{
    public enum Unit { MM, CM, M, INCH, FT, DEG, RAD }
    public enum FaceSide { FRONT, BACK, BOTH }

    /// <summary>
    /// Christophs Debugger
    /// </summary>
    public static class BIMDebugLog
    {
        public static List<string> logStrings = new List<string>();

        public static void Log(string log)
        {
            logStrings.Add(log);
        }

        public static string GetLog()
        {
            return string.Join("\n", logStrings);
        }

        public static string GetLogCondenced()
        {
            Dictionary<string, int> dict = new Dictionary<string, int>();
            foreach (string s in logStrings)
            {
                dict[s] = dict.ContainsKey(s) ? dict[s] + 1 : 1;
            }
            return string.Join("\n", dict.Select(kvp => kvp.Value.ToString() + ": \t" + kvp.Key));
        }

        public static void ResetLog()
        {
            logStrings = new List<string>();
        }
    }

    public class Matrix4
    {
        private double[,] matrix = new double[4, 4];

        public static int Dim = 4;

        public Matrix4(Vector4d c1, Vector4d c2, Vector4d c3, Vector4d c4)
        {
            SetColumn(0, c1);
            SetColumn(1, c2);
            SetColumn(2, c3);
            SetColumn(3, c4);
        }

        public Matrix4(double[] values)
        {
            if (values.Length != Dim * Dim)
            {
                throw new Exception("Must contain exactly " + Dim * Dim + " values");
            }
            for (int i = 0; i < Dim; i++)
            {
                for (int j = 0; j < Dim; j++)
                {
                    matrix[i, j] = values[i * 4 + j];
                }
            }
        }

        public Matrix4()
        {
            SetColumn(0, new Vector4d());
            SetColumn(1, new Vector4d());
            SetColumn(2, new Vector4d());
            SetColumn(3, new Vector4d());
        }

        public Vector4d GetColumn(int index)
        {
            return new Vector4d(matrix[0, index],
                                matrix[1, index],
                                matrix[2, index],
                                matrix[3, index]);
        }

        public Vector4d GetRow(int index)
        {
            return new Vector4d(matrix[index, 0],
                                matrix[index, 1],
                                matrix[index, 2],
                                matrix[index, 3]);
        }

        public double GetCell(int rowIndex, int columnIndex)
        {
            return matrix[rowIndex, columnIndex];
        }

        public void SetColumn(int index, Vector4d v)
        {
            matrix[0, index] = v.x;
            matrix[1, index] = v.y;
            matrix[2, index] = v.z;
            matrix[3, index] = v.w;
        }

        public void SetRow(int index, Vector4d v)
        {
            matrix[index, 0] = v.x;
            matrix[index, 1] = v.y;
            matrix[index, 2] = v.z;
            matrix[index, 3] = v.w;
        }

        public void SetCell(int rowIndex, int columnIndex, double v)
        {
            matrix[rowIndex, columnIndex] = v;
        }

        public Matrix4 GetTranspose()
        {
            Matrix4 matrix4 = new Matrix4();
            matrix4.SetRow(0, GetColumn(0));
            matrix4.SetRow(1, GetColumn(1));
            matrix4.SetRow(2, GetColumn(2));
            matrix4.SetRow(3, GetColumn(3));

            return matrix4;
        }

        public Matrix4 GetInverse(out bool hasInverse)
        {
            return MatrixInverse(this, out hasInverse);
        }

        // Static:

        public static Matrix4 IdentityMatrix()
        {
            return new Matrix4(
                                new Vector4d(1, 0, 0, 0),
                                new Vector4d(0, 1, 0, 0),
                                new Vector4d(0, 0, 1, 0),
                                new Vector4d(0, 0, 0, 1)
                                );
        }

        public static Vector4d Multiply(Matrix4 m, Vector4d v)
        {
            double[] sum = new double[] { 0, 0, 0, 0 };
            for (int rowNum = 0; rowNum < 4; rowNum++)
            {
                sum[rowNum] = Vector4d.Dot(m.GetRow(rowNum), v);
            }

            return new Vector4d(sum);
        }

        public static Matrix4 Multiply(Matrix4 m1, Matrix4 m2)
        {
            List<Vector4d> newColList = new List<Vector4d>();
            for (int m2ColNum = 0; m2ColNum < 4; m2ColNum++)
            {
                newColList.Add(Multiply(m1, m2.GetColumn(m2ColNum)));
            }

            return new Matrix4(newColList[0], newColList[1], newColList[2], newColList[3]);
        }

        public string Print()
        {
            List<string> rowStrings = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                List<string> columnStrings = new List<string>();
                for (int j = 0; j < 4; j++)
                {
                    columnStrings.Add(matrix[i, j].ToString());
                }
                rowStrings.Add("[ " + string.Join(", \t", columnStrings) + "]");
            }
            return string.Join("\n", rowStrings);
        }

        // http://quaetrix.com/Matrix/code.html
        public static Matrix4 MatrixInverse(Matrix4 matrix, out bool hasInverse)
        {
            hasInverse = true;
            if (MatrixDeterminant(matrix) == 0)
            {
                hasInverse = false;
                return matrix;
            }

            // assumes determinant is not 0
            // that is, the matrix does have an inverse
            int n = Dim;
            Matrix4 result = new Matrix4(); // make a copy of matrix
            for (int i = 0; i < n; ++i)
                for (int j = 0; j < n; ++j)
                    result.SetCell(i, j, matrix.GetCell(i, j));

            Matrix4 lum; // combined lower & upper
            int[] perm;
            int toggle;
            toggle = MatrixDecompose(matrix, out lum, out perm);

            double[] b = new double[n];
            for (int i = 0; i < n; ++i)
            {
                for (int j = 0; j < n; ++j)
                    if (i == perm[j])
                        b[j] = 1.0f;
                    else
                        b[j] = 0.0f;

                double[] x = Helper(lum, b); // 
                for (int j = 0; j < n; ++j)
                    result.SetCell(j, i, x[j]);
            }
            return result;
        } // MatrixInverse

        public static int MatrixDecompose(Matrix4 m, out Matrix4 lum, out int[] perm)
        {
            // Crout's LU decomposition for matrix determinant and inverse
            // stores combined lower & upper in lum[][]
            // stores row permuations into perm[]
            // returns +1 or -1 according to even or odd number of row permutations
            // lower gets dummy 1.0s on diagonal (0.0s above)
            // upper gets lum values on diagonal (0.0s below)

            int toggle = +1; // even (+1) or odd (-1) row permutatuions
            int n = Dim;

            // make a copy of m[][] into result lu[][]
            lum = new Matrix4();
            for (int i = 0; i < n; ++i)
            {
                for (int j = 0; j < n; ++j)
                {
                    lum.SetCell(i, j, m.GetCell(i, j));
                }
            }

            // make perm[]
            perm = new int[n];
            for (int i = 0; i < n; ++i)
                perm[i] = i;

            for (int j = 0; j < n - 1; ++j) // process by column. note n-1 
            {
                double max = Math.Abs(lum.GetCell(j, j));
                int piv = j;

                for (int i = j + 1; i < n; ++i) // find pivot index
                {
                    double xij = Math.Abs(lum.GetCell(i, j));
                    if (xij > max)
                    {
                        max = xij;
                        piv = i;
                    }
                } // i

                if (piv != j)
                {
                    Vector4d tmp = lum.GetRow(piv); // swap rows j, piv
                    lum.SetRow(piv, lum.GetRow(j));
                    lum.SetRow(j, tmp);

                    int t = perm[piv]; // swap perm elements
                    perm[piv] = perm[j];
                    perm[j] = t;

                    toggle = -toggle;
                }

                double xjj = lum.GetCell(j, j);
                if (xjj != 0.0)
                {
                    for (int i = j + 1; i < n; ++i)
                    {
                        double xij = lum.GetCell(i, j) / xjj;
                        lum.SetCell(i, j, xij);
                        for (int k = j + 1; k < n; ++k)
                            lum.SetCell(i, k, lum.GetCell(i, k) - xij * lum.GetCell(j, k));
                    }
                }

            } // j

            return toggle;
        } // MatrixDecompose

        public static double[] Helper(Matrix4 luMatrix, double[] b)
        {
            int n = Dim;
            double[] x = new double[n];
            b.CopyTo(x, 0);

            for (int i = 1; i < n; ++i)
            {
                double sum = x[i];
                for (int j = 0; j < i; ++j)
                    sum -= luMatrix.GetCell(i, j) * x[j];
                x[i] = sum;
            }

            x[n - 1] /= luMatrix.GetCell(n - 1, n - 1);
            for (int i = n - 2; i >= 0; --i)
            {
                double sum = x[i];
                for (int j = i + 1; j < n; ++j)
                    sum -= luMatrix.GetCell(i, j) * x[j];
                x[i] = sum / luMatrix.GetCell(i, i);
            }

            return x;
        } // Helper

        public static double MatrixDeterminant(Matrix4 matrix)
        {
            Matrix4 lum;
            int[] perm;
            int toggle = MatrixDecompose(matrix, out lum, out perm);
            double result = toggle;
            for (int i = 0; i < Dim; ++i)
                result *= lum.GetCell(i, i);
            return result;
        }

        public static bool Equal(Matrix4 m1, Matrix4 m2)
        {
            for (int i = 0; i < Dim; i++)
            {
                for (int j = 0; j < Dim; j++)
                {
                    if (m1.GetCell(i, j) != m2.GetCell(i, j))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Just helpful methods for some Math and Mesh operations (Most still require descriptions so my bad :))
    /// </summary>
    public static class GeomUtils
    {
        // General Note: Triangle use right hand rule for facing direction

        public static double INCH_TO_METER = 0.0254f;
        public static double FT_TO_METER = 0.3048f;
        public static double CM_TO_METER = 0.01f;
        public static double MM_TO_METER = 0.001f;

        private static Random random = new Random();

        public static void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = random.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public static Tuple<double, double> ElbowMethod(List<Tuple<double, double>> pairsList)
        {
            pairsList = pairsList.OrderBy(p => p.Item1).ToList();

            Tuple<double, double> elbowVal = pairsList[0];
            double biggestRateChange = double.MinValue;
            for (int i = 1; i < pairsList.Count - 1; i++)
            {
                double rateBefore = Math.Abs((pairsList[i].Item2 - pairsList[i - 1].Item2) / (pairsList[i].Item1 - pairsList[i - 1].Item1));
                double rateAfter = Math.Abs((pairsList[i + 1].Item2 - pairsList[i].Item2) / (pairsList[i + 1].Item1 - pairsList[i].Item1));
                double rateChange = rateBefore == 0 ? 0 : rateAfter / rateBefore;
                if (rateChange > biggestRateChange)
                {
                    biggestRateChange = rateChange;
                    elbowVal = pairsList[i];
                }
            }

            return elbowVal;
        }

        public static Tuple<double, double> FirstMajorElbowMethod(List<Tuple<double, double>> pairsList)
        {
            pairsList = pairsList.OrderBy(p => p.Item1).ToList();

            List<double> rateChanges = new List<double>();
            rateChanges.Add(Math.Abs((pairsList[1].Item2 - pairsList[0].Item2) / (pairsList[1].Item1 - pairsList[0].Item1)));
            for (int i = 1; i < pairsList.Count - 1; i++)
            {
                double rateBefore = Math.Abs((pairsList[i].Item2 - pairsList[i - 1].Item2) / (pairsList[i].Item1 - pairsList[i - 1].Item1));
                double rateAfter = Math.Abs((pairsList[i + 1].Item2 - pairsList[i].Item2) / (pairsList[i + 1].Item1 - pairsList[i].Item1));
                double rateChange = rateBefore == 0 ? rateAfter : rateAfter / rateBefore;
                rateChanges.Add(rateChange);
            }
            rateChanges.Add(0);// 0 / Math.Abs((pairsList[pairsList.Count-1].Item2 - pairsList[pairsList.Count-2].Item2) / (pairsList[pairsList.Count-1].Item1 - pairsList[pairsList.Count-2].Item1)));

            double meanRateChange = rateChanges.Average();
            for (int i = 0; i < pairsList.Count; i++)
            {
                if (rateChanges[i] > meanRateChange)
                {
                    return pairsList[i];
                }
            }

            return pairsList[0]; ;
        }

        public static Tuple<double, double> ClosestToAverageSelector(List<Tuple<double, double>> pairsList)
        {
            double averageY = pairsList.Average(y => y.Item2);

            Tuple<double, double> best = pairsList[0];
            double bestVarriance = Math.Abs(best.Item2 - averageY);
            for (int i = 1; i < pairsList.Count; i++)
            {
                Tuple<double, double> next = pairsList[i];
                double var = Math.Abs(next.Item2 - averageY);
                if (bestVarriance > var)
                {
                    bestVarriance = var;
                    best = next;
                }
            }

            return best;
        }

        public static Tuple<double, double> MinYSelector(List<Tuple<double, double>> pairsList)
        {
            Tuple<double, double> best = pairsList[0];
            double bestY = best.Item2;
            for (int i = 1; i < pairsList.Count; i++)
            {
                Tuple<double, double> next = pairsList[i];
                double y = next.Item2;
                if (bestY > y)
                {
                    bestY = y;
                    best = next;
                }
            }

            return best;
        }

        public static Dictionary<string, double> FindAveragePlaceOrder(List<List<string>> placeOrders)
        {
            Dictionary<string, double> ranking = new Dictionary<string, double>();
            List<string> allTypes = placeOrders.SelectMany(t => t).Distinct().ToList();
            foreach (string item in allTypes)
            {
                double sum = 0;
                foreach (List<string> placeOrder in placeOrders)
                {
                    if (placeOrder.Contains(item))
                    {
                        sum += placeOrder.IndexOf(item);
                    }
                }

                ranking.Add(item, Math.Round(sum / placeOrders.Count, 2));
            }

            return ranking;
        }

        public static double ChangeUnitToMeterOrDeg(double value, Unit unit)
        {
            switch (unit)
            {
                case (Unit.MM):
                    return value * MM_TO_METER;
                case (Unit.CM):
                    return value * MM_TO_METER;
                case (Unit.M):
                    return value * 1.0;
                case (Unit.INCH):
                    return value * INCH_TO_METER;
                case (Unit.FT):
                    return value * FT_TO_METER;
                case (Unit.DEG):
                    return value * Math.PI / 180;
                case (Unit.RAD):
                    return value;
            }
            return value;
        }
        public enum Units
        {
            MM, CM, M, Inch, Ft
        }
        public static double precision = 0.0001;
        public static double GetScale(Units unit)
        {
            double scale = 1.0;
            switch (unit)
            {
                case (Units.MM):
                    scale = 1.0 / 1000.0;
                    break;
                case (Units.CM):
                    scale = 1.0 / 100.0;
                    break;
                case (Units.M):
                    scale = 1.0 / 1.0;
                    break;
                case (Units.Inch):
                    scale = 1.0 / 39.3701;
                    break;
                case (Units.Ft):
                    scale = 1.0 / 3.28084;
                    break;
            }
            return scale;
        }
        public static Units GetUnit(double scale)
        {
            if (Math.Abs(scale - 1.0 / 1000.0) < precision)
            {
                return Units.MM;
            }
            if (Math.Abs(scale - 1.0 / 100.0) < precision)
            {
                return Units.CM;
            }
            if (Math.Abs(scale - 1.0 / 1.0) < precision)
            {
                return Units.M;
            }
            if (Math.Abs(scale - 1.0 / 39.3701) < precision)
            {
                return Units.Inch;
            }
            if (Math.Abs(scale - 1.0 / 3.28084) < precision)
            {
                return Units.Ft;
            }
            return Units.M;
        }

        public static double RandomGausian(double mean, double std)
        {
            double u1 = 1.0 - random.NextDouble(); //uniform(0,1] random doubles
            double u2 = 1.0 - random.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0f * Math.Log(u1)) * Math.Sin(2.0f * Math.PI * u2); //random normal(0,1)
            double randNormal = mean + std * randStdNormal; //random normal(mean,stdDev^2)

            return randNormal;
        }

        public static void GetStatisticsFromList(List<double> items, out double mean, out double std, out double confInt95, bool sample = true)
        {
            double average = items.Average();
            double sumOfSquare = items.Select(i => Math.Pow(i - average, 2.0)).Sum();

            mean = average;
            int count = sample ? items.Count - 1 : items.Count;
            std = Math.Sqrt(sumOfSquare / count);
            confInt95 = 1.96 * std / Math.Sqrt(items.Count);
        }

        public static void GetXYZDimentions(List<Vector3d> localVectors, out Vector3d center, out Vector3d dimentions)
        {
            if (localVectors.Count == 0)
            {
                center = new Vector3d();
                dimentions = new Vector3d();
                return;
            }
            double minX = localVectors.Min(v => v.x);
            double minY = localVectors.Min(v => v.y);
            double minZ = localVectors.Min(v => v.z);
            double maxX = localVectors.Max(v => v.x);
            double maxY = localVectors.Max(v => v.y);
            double maxZ = localVectors.Max(v => v.z);
            center = new Vector3d((maxX + minX) / 2.0, (maxY + minY) / 2.0, (maxZ + minZ) / 2.0);
            dimentions = new Vector3d(maxX - minX, maxY - minY, maxZ - minZ);
        }

        public static double GetMeshRadius(List<Vector3d> localVectors)
        {
            if (localVectors.Count <= 1)
            {
                return 0.0;
            }

            double minX = localVectors.Min(v => v.x);
            double minY = localVectors.Min(v => v.y);
            double minZ = localVectors.Min(v => v.z);
            double maxX = localVectors.Max(v => v.x);
            double maxY = localVectors.Max(v => v.y);
            double maxZ = localVectors.Max(v => v.z);
            Vector3d center = new Vector3d((maxX + minX) / 2.0, (maxY + minY) / 2.0, (maxZ + minZ) / 2.0);
            double radius = localVectors.Max(v => v.Distance(center));
            return radius;
        }

        public static List<Vector3d> ShrinkShape(IEnumerable<Vector3d> vects, double ratio)
        {
            List<Vector3d> returnList = new List<Vector3d>();
            if (vects.Count() == 0)
            {
                return returnList;
            }

            double minVx = vects.Min(v => v.x);
            double maxVx = vects.Max(v => v.x);
            double minVy = vects.Min(v => v.y);
            double maxVy = vects.Max(v => v.y);
            double minVz = vects.Min(v => v.z);
            double maxVz = vects.Max(v => v.z);
            Vector3d centerV = new Vector3d((maxVx + minVx) / 2, (maxVy + minVy) / 2, (maxVz + minVz) / 2);
            foreach (Vector3d v in vects)
            {
                Vector3d diff = v - centerV;
                returnList.Add(centerV + (diff * ratio));
            }
            return returnList;
        }

        public static DMesh3 ShrinkMesh(DMesh3 m, double ratio)
        {
            if (ratio == 1.0)
            {
                return m;
            }
            List<Vector3d> newVects = ShrinkShape(m.Vertices(), ratio);
            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(newVects, m.Triangles());
        }

        public static List<Vector3d> TranslateVerticies(Matrix4 translationMatrix, List<Vector3d> verticies)
        {
            List<Vector3d> newVertices = new List<Vector3d>();
            foreach (Vector3d vect in verticies)
            {
                Vector4d vect4 = Matrix4.Multiply(translationMatrix, new Vector4d(vect.x, vect.y, vect.z, 1.0));
                newVertices.Add(new Vector3d(vect4.x, vect4.y, vect4.z));
            }

            return newVertices;
        }

        public static DMesh3 TranslateMesh(Matrix4 translationMatrix, DMesh3 mesh)
        {
            List<Vector3d> newVects = TranslateVerticies(translationMatrix, mesh.Vertices().ToList());
            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(newVects, mesh.Triangles());
        }

        public static Matrix4 GetTranslationMatrixFromLocationOrientation(Vector3d location, Vector4d orientation)
        {
            Vector4d o = orientation;
            Vector3d c1 = new Vector3d(1 - 2 * Math.Pow(o.y, 2) - 2 * Math.Pow(o.z, 2), 2 * (o.x * o.y + o.z * o.w), 2 * (o.x * o.z - o.y * o.w));
            Vector3d c2 = new Vector3d(2 * (o.x * o.y - o.z * o.w), 1 - 2 * Math.Pow(o.x, 2) - 2 * Math.Pow(o.z, 2), 2 * (o.x * o.w + o.y * o.z));
            Vector3d c3 = new Vector3d(2 * (o.x * o.z + o.y * o.w), 2 * (o.y * o.z - o.x * o.w), 1 - 2 * Math.Pow(o.x, 2) - 2 * Math.Pow(o.y, 2));

            // https://en.wikipedia.org/wiki/Quaternions_and_spatial_rotation (note that this matrix is custructed by columns...)
            return new Matrix4(new Vector4d(c1.x, c1.y, c1.z, location.x),
                               new Vector4d(c2.x, c2.y, c2.z, location.y),
                               new Vector4d(c3.x, c3.y, c3.z, location.z),
                               new Vector4d(0, 0, 0, 1)).GetTranspose();
        }

        public static void GetLocationAndOrientationFromTranslationMatrix(Matrix4 matrix, out Vector3d location, out Vector4d orientation)
        {
            location = new Vector3d(matrix.GetCell(0, 3), matrix.GetCell(1, 3), matrix.GetCell(2, 3));
            orientation = new Vector4d();

            // http://www.euclideanspace.com/maths/geometry/rotations/conversions/matrixToQuaternion/index.htm
            double tr = matrix.GetCell(0, 0) + matrix.GetCell(1, 1) + matrix.GetCell(2, 2);
            if (tr > 0)
            {
                double S = Math.Sqrt(tr + 1.0) * 2.0; // S=4*qw 
                orientation.w = 0.25 * S;
                orientation.x = (matrix.GetCell(2, 1) - matrix.GetCell(1, 2)) / S;
                orientation.y = (matrix.GetCell(0, 2) - matrix.GetCell(2, 0)) / S;
                orientation.z = (matrix.GetCell(1, 0) - matrix.GetCell(0, 1)) / S;
            }
            else if ((matrix.GetCell(0, 0) > matrix.GetCell(1, 1)) & (matrix.GetCell(0, 0) > matrix.GetCell(2, 2)))
            {
                double S = Math.Sqrt(1.0f + matrix.GetCell(0, 0) - matrix.GetCell(1, 1) - matrix.GetCell(2, 2)) * 2.0; // S=4*qx 
                orientation.w = (matrix.GetCell(2, 1) - matrix.GetCell(1, 2)) / S;
                orientation.x = 0.25 * S;
                orientation.y = (matrix.GetCell(0, 1) + matrix.GetCell(1, 0)) / S;
                orientation.z = (matrix.GetCell(0, 2) + matrix.GetCell(2, 0)) / S;
            }
            else if (matrix.GetCell(1, 1) > matrix.GetCell(2, 2))
            {
                double S = Math.Sqrt(1.0f + matrix.GetCell(1, 1) - matrix.GetCell(0, 0) - matrix.GetCell(2, 2)) * 2.0; // S=4*qy
                orientation.w = (matrix.GetCell(0, 2) - matrix.GetCell(2, 0)) / S;
                orientation.x = (matrix.GetCell(0, 1) + matrix.GetCell(1, 0)) / S;
                orientation.y = 0.25 * S;
                orientation.z = (matrix.GetCell(1, 2) + matrix.GetCell(2, 1)) / S;
            }
            else
            {
                double S = Math.Sqrt(1.0f + matrix.GetCell(2, 2) - matrix.GetCell(0, 0) - matrix.GetCell(1, 1)) * 2.0; // S=4*qz
                orientation.w = (matrix.GetCell(1, 0) - matrix.GetCell(0, 1)) / S;
                orientation.x = (matrix.GetCell(0, 2) + matrix.GetCell(2, 0)) / S;
                orientation.y = (matrix.GetCell(1, 2) + matrix.GetCell(2, 1)) / S;
                orientation.z = 0.25 * S;
            }
        }

        public static Vector4d GetQuaterion(Vector3d axis, double rotationRad)
        {
            // https://en.wikipedia.org/wiki/Quaternions_and_spatial_rotation#Using_quaternion_as_rotations
            axis.Normalize();
            double C = Math.Cos(rotationRad / 2.0);
            double S = Math.Sin(rotationRad / 2.0);
            return new Vector4d(axis.x * S, axis.y * S, axis.z * S, C);
        }

        public static void GetAxisAngleFromQuanterion(Vector4d quanterion, out Vector3d axis, out double rotationRad)
        {
            // https://en.wikipedia.org/wiki/Quaternions_and_spatial_rotation#Using_quaternion_as_rotations
            double A = Math.Sqrt(quanterion.x * quanterion.x + quanterion.y * quanterion.y + quanterion.z * quanterion.z);
            axis = new Vector3d(quanterion.x / A, quanterion.y / A, quanterion.z / A);
            rotationRad = 2.0f * Math.Atan2(A, quanterion.w);
            return;
        }

        public static Vector3d EstimateFaceNormal(List<Vector3d> points)
        {
            Vector3d norm = new Vector3d();
            for (int i = 0; i < points.Count; i++)
            {
                int index1 = (i + 0) % points.Count;
                int index2 = (i + 1) % points.Count;
                int index3 = (i + 2) % points.Count;
                Vector3d p1 = points[index1];
                Vector3d p2 = points[index2];
                Vector3d p3 = points[index3];

                Vector3d v1 = (p2 - p1);
                Vector3d v2 = (p2 - p3);
                Vector3d dir = Vector3d.Cross(v1, v2);
                norm = (norm + dir);
            }
            norm.Normalize();
            return norm;
        }

        public static Vector3d RotatePointAroundZAxis(Vector3d point, double angleInDegrees)
        {
            double angleInRadians = angleInDegrees * (Math.PI / 180.0f);
            double cosTheta = Math.Cos(angleInRadians);
            double sinTheta = Math.Sin(angleInRadians);
            Vector3d newPoint = new Vector3d(
                                    point.x * cosTheta - point.y * sinTheta,
                                    point.x * sinTheta + point.y * cosTheta,
                                    point.z);
            return newPoint;
        }

        public static double TriangleArea(Vector3d a, Vector3d b, Vector3d c)
        {
            return Vector3d.Cross((b - a), (c - a)).Length / 2.0;
        }

        public static bool PointInTriangleByArea(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            double A = TriangleArea(a, b, c);
            double A1 = TriangleArea(p, b, c);
            double A2 = TriangleArea(a, p, c);
            double A3 = TriangleArea(a, b, p);
            return Math.Abs(A - (A1 + A2 + A3)) < double.Epsilon;
        }

        public static bool PointInTriangle(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
        {
            if (SameSide(p, a, b, c) && SameSide(p, b, a, c) && SameSide(p, c, a, b))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool SameSide(Vector3d p1, Vector3d p2, Vector3d a, Vector3d b)
        {
            Vector3d cp1 = Vector3d.Cross((b - a), (p1 - a));
            Vector3d cp2 = Vector3d.Cross((b - a), (p2 - a));
            if (Vector3d.Dot(cp1, cp2) >= 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static double MeshDistance(DMesh3 m1, DMesh3 m2, out Vector3d m1Point, out Vector3d m2Point)
        {
            var m1Verts = m1.Vertices();
            var m2Verts = m2.Vertices();

            double minDist = double.MaxValue;
            m1Point = new Vector3d();
            m2Point = new Vector3d();
            foreach (var tri1 in m1.Triangles())
            {
                Vector3d v0 = m1Verts.ElementAt(tri1.a);
                Vector3d v1 = m1Verts.ElementAt(tri1.b);
                Vector3d v2 = m1Verts.ElementAt(tri1.c);

                foreach (var tri2 in m2.Triangles())
                {
                    Vector3d u0 = m2Verts.ElementAt(tri2.a);
                    Vector3d u1 = m2Verts.ElementAt(tri2.b);
                    Vector3d u2 = m2Verts.ElementAt(tri2.c);

                    // Get the min distance of each triangle pair:
                    DistTriangle3Triangle3 distTriangle3Triangle3 = new DistTriangle3Triangle3(new Triangle3d(v0, v1, v2), new Triangle3d(u0, u1, u2));
                    double dist = distTriangle3Triangle3.Get();
                    if (double.IsNaN(dist))
                    {
                        continue;
                    }
                    if (minDist > dist)
                    {
                        minDist = dist;
                        m1Point = distTriangle3Triangle3.Triangle0Closest;
                        m2Point = distTriangle3Triangle3.Triangle1Closest;
                    }
                }
            }

            return minDist;
        }

        public static Vector3d ClosestPointOnMeshToPoint(Vector3d v, DMesh3 mesh)
        {
            Vector3d closestPointMin = new Vector3d();
            double minDist = double.MaxValue;
            var m1Verts = mesh.Vertices();
            foreach (var tri1 in mesh.Triangles())
            {
                Vector3d v0 = m1Verts.ElementAt(tri1.a);
                Vector3d v1 = m1Verts.ElementAt(tri1.b);
                Vector3d v2 = m1Verts.ElementAt(tri1.c);

                Triangle3d t = new Triangle3d(v0, v1, v2);
                double distSqr = DistPoint3Triangle3.DistanceSqr(ref v, ref t, out Vector3d closestPoint, out Vector3d bayCoords);

                DistPoint3Triangle3 distPoint3Triangle3 = new DistPoint3Triangle3(v, t);
                double dist = distPoint3Triangle3.Get();
                if (double.IsNaN(dist))
                {
                    continue;
                }

                if (minDist > dist)
                {
                    minDist = dist;
                    closestPointMin = closestPoint;
                }
            }

            return closestPointMin;
        }

        public static bool MeshOverlap(DMesh3 m1, DMesh3 m2, double shrinkRatio = 1.0)
        {
            DMesh3 m1Shrink = m1;
            DMesh3 m2Shrink = m2;
            if (shrinkRatio != 1.0)
            {
                m1Shrink = ShrinkMesh(m1, shrinkRatio);
                m2Shrink = ShrinkMesh(m2, shrinkRatio);
            }

            var m1Verts = m1Shrink.Vertices();
            var m2Verts = m2Shrink.Vertices();
            foreach (var tri1 in m1Shrink.Triangles())
            {
                Vector3d v0 = m1Verts.ElementAt(tri1.a);
                Vector3d v1 = m1Verts.ElementAt(tri1.b);
                Vector3d v2 = m1Verts.ElementAt(tri1.c);

                foreach (var tri2 in m2Shrink.Triangles())
                {
                    Vector3d u0 = m2Verts.ElementAt(tri2.a);
                    Vector3d u1 = m2Verts.ElementAt(tri2.b);
                    Vector3d u2 = m2Verts.ElementAt(tri2.c);

                    IntrTriangle3Triangle3 intrTriangle3Triangle3 = new IntrTriangle3Triangle3(new Triangle3d(v0, v1, v2), new Triangle3d(u0, u1, u2));

                    bool testResult1 = intrTriangle3Triangle3.Find();
                    if (testResult1) { return true; }
                }
            }

            return MeshInsideMesh(m1Shrink, m2Shrink);
        }

        public static bool MeshInsideMesh(DMesh3 m1, DMesh3 m2)
        {
            Vector3d intP;
            var verts1 = m1.Vertices();

            // Next Two check if one is fully inside the other
            Vector3d centerM1 = new Vector3d(verts1.ToArray().Average(v => v.x), verts1.ToArray().Average(v => v.y), verts1.ToArray().Average(v => v.z));
            if (RayIntersectsMesh(centerM1, new Vector3d(0, 0, 1), m2, out intP) &&
                RayIntersectsMesh(centerM1, new Vector3d(0, 0, -1), m2, out intP) &&
                RayIntersectsMesh(centerM1, new Vector3d(0, 1, 0), m2, out intP) &&
                RayIntersectsMesh(centerM1, new Vector3d(0, -1, 0), m2, out intP) &&
                RayIntersectsMesh(centerM1, new Vector3d(1, 0, 0), m2, out intP) &&
                RayIntersectsMesh(centerM1, new Vector3d(-1, 0, 0), m2, out intP))
            {
                return true;
            }

            var verts2 = m2.Vertices();
            Vector3d centerM2 = new Vector3d(verts2.ToArray().Average(v => v.x), verts2.ToArray().Average(v => v.y), verts2.ToArray().Average(v => v.z));
            if (RayIntersectsMesh(centerM2, new Vector3d(0, 0, 1), m1, out intP) &&
                RayIntersectsMesh(centerM2, new Vector3d(0, 0, -1), m1, out intP) &&
                RayIntersectsMesh(centerM2, new Vector3d(0, 1, 0), m1, out intP) &&
                RayIntersectsMesh(centerM2, new Vector3d(0, -1, 0), m1, out intP) &&
                RayIntersectsMesh(centerM2, new Vector3d(1, 0, 0), m1, out intP) &&
                RayIntersectsMesh(centerM2, new Vector3d(-1, 0, 0), m1, out intP))
            {
                return true;
            }

            return false;
        }

        public static double PointToTriPlanDistance(Vector3d v, List<Vector3d> tri)
        {
            Vector3d normal = Vector3d.Cross((tri[1] - tri[0]), (tri[2] - tri[0]));
            double w = -Vector3d.Dot(tri[0], normal);
            Vector4d planeEq = new Vector4d(normal.x, normal.y, normal.z, w);

            double dist = Math.Abs(Vector4d.Dot(new Vector4d(normal.x, normal.y, normal.z, 1), planeEq)) / normal.Length;
            return dist;
        }

        public static bool ValueInRange(double a, double b, double value)
        {
            return (value < a && value > b) || (value < b && value > a);
        }

        public static bool RayIntersectsTriangle(Vector3d rayOrigin, Vector3d rayVector, Vector3d vertex0, Vector3d vertex1, Vector3d vertex2, out Vector3d intersectionPoint)
        {
            intersectionPoint = new Vector3d();
            // https://en.wikipedia.org/wiki/M%C3%B6ller%E2%80%93Trumbore_intersection_algorithm

            const double EPSILON = 0.0000001;
            Vector3d edge1 = (vertex1 - vertex0);
            Vector3d edge2 = (vertex2 - vertex0);
            Vector3d h = Vector3d.Cross(rayVector, edge2);
            double a = Vector3d.Dot(edge1, h);
            if (a > -EPSILON && a < EPSILON)
                return false;    // This ray is parallel to this triangle.
            double f = 1.0f / a;
            Vector3d s = (rayOrigin - vertex0);
            double u = f * (Vector3d.Dot(s, h));
            if (u < 0.0f || u > 1.0)
            {
                return false;
            }
            Vector3d q = Vector3d.Cross(s, edge1);
            double v = f * Vector3d.Dot(rayVector, q);
            if (v < 0.0f || u + v > 1.0)
            {
                return false;
            }
            // At this stage we can compute t to find out where the intersection point is on the line.
            double t = f * Vector3d.Dot(edge2, q);
            if (t > EPSILON) // ray intersection
            {
                intersectionPoint = (rayOrigin + (rayVector * t));
                return true;
            }
            else // This means that there is a line intersection but not a ray intersection.
            {
                return false;
            }
        }

        public static bool RayIntersectsMesh(Vector3d rayOrigin, Vector3d rayVector, DMesh3 m, out Vector3d intersectionPoint, bool fast = true)
        {
            bool intersects = false;
            intersectionPoint = new Vector3d();
            double closestIntPoint = double.MaxValue;

            var m1Verts = m.Vertices();
            foreach (var tri1 in m.Triangles())
            {
                Vector3d v0 = m1Verts.ElementAt(tri1.a);
                Vector3d v2 = m1Verts.ElementAt(tri1.b);
                Vector3d v1 = m1Verts.ElementAt(tri1.c);

                bool testResult = RayIntersectsTriangle(rayOrigin, rayVector, v0, v1, v2, out Vector3d triIntPoint);
                if (testResult)
                {
                    double triIntDist = triIntPoint.Distance(rayOrigin);
                    if (closestIntPoint > triIntDist)
                    {
                        closestIntPoint = triIntDist;
                        intersectionPoint = triIntPoint;
                    }
                }

                intersects = intersects || testResult;
                if (fast && intersects) // If you set fast true then you will break out and not get the true intersectionPoint, only the first one
                {
                    break;
                }
            }

            return intersects;
        }

        public static List<DMesh3> CenterMeshCollection(List<DMesh3> meshes, out Vector3d center)
        {
            var verticies = meshes.SelectMany(m => m.Vertices()).ToList();
            GetXYZDimentions(verticies, out Vector3d newCenter, out Vector3d dims);

            List<DMesh3> newMeshes = new List<DMesh3>();
            foreach (var m in meshes)
            {
                newMeshes.Add(DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(m.Vertices().Select(v => v - newCenter), m.Triangles()));
            }
            center = newCenter;
            return newMeshes;
        }

        public static DMesh3 CreateBoundingBox(Vector3d location, Vector3d dimentions, FaceSide faceSide = FaceSide.FRONT)
        {
            List<Vector3d> cornerList = new List<Vector3d>
            {
                new Vector3d(location.x-dimentions.x / 2.0, location.y-dimentions.y / 2.0, location.z-dimentions.z / 2.0),
                new Vector3d(location.x+dimentions.x / 2.0, location.y-dimentions.y / 2.0, location.z-dimentions.z / 2.0),
                new Vector3d(location.x+dimentions.x / 2.0, location.y+dimentions.y / 2.0, location.z-dimentions.z / 2.0),
                new Vector3d(location.x-dimentions.x / 2.0, location.y+dimentions.y / 2.0, location.z-dimentions.z / 2.0),
                new Vector3d(location.x-dimentions.x / 2.0, location.y-dimentions.y / 2.0, location.z+dimentions.z / 2.0),
                new Vector3d(location.x+dimentions.x / 2.0, location.y-dimentions.y / 2.0, location.z+dimentions.z / 2.0),
                new Vector3d(location.x+dimentions.x / 2.0, location.y+dimentions.y / 2.0, location.z+dimentions.z / 2.0),
                new Vector3d(location.x-dimentions.x / 2.0, location.y+dimentions.y / 2.0, location.z+dimentions.z / 2.0)
            };

            List<Index3i> tList = new List<Index3i>
            {
                new Index3i(0, 1, 2),
                new Index3i(0, 2, 3),
                new Index3i(4, 7, 6),
                new Index3i(4, 6, 5),
                new Index3i(0, 4, 1),
                new Index3i(1, 4, 5),
                new Index3i(1, 5, 2),
                new Index3i(2, 5, 6),
                new Index3i(2, 6, 3),
                new Index3i(3, 6, 7),
                new Index3i(3, 7, 0),
                new Index3i(0, 7, 4)
            };

            List<Index3i> triangles = new List<Index3i>();
            if (faceSide == FaceSide.FRONT || faceSide == FaceSide.BOTH)
            {
                triangles.AddRange(tList);
            }
            if (faceSide == FaceSide.BACK || faceSide == FaceSide.BOTH)
            {
                triangles.AddRange(tList.Select(tl => new Index3i(tl.array.Reverse().ToArray())));
            }

            return MeshFixUtils.VertexAdderByTri(DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(cornerList, triangles));
        }
    }

    public static class MeshFixUtils
    {
        public static DMesh3 MeshReduce(DMesh3 mesh, int reduceCount)
        {
            // http://www.gradientspace.com/tutorials/2017/8/30/mesh-simplification
            List<Vector3d> normals = CalculateNormals(mesh);
            DMesh3 meshCopy = DMesh3Builder.Build(mesh.Vertices(), mesh.Triangles(), normals);
            bool check = meshCopy.CheckValidity(false, FailMode.Throw);
            Reducer r = new Reducer(meshCopy);
            r.ReduceToTriangleCount(reduceCount);

            //MeshEditor.RemoveUnusedVertices(r.Mesh);
            //return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(mesh.Vertices(), r.Mesh.Triangles());

            List<Vector3d> verts = mesh.Vertices().Select(v => new Vector3d(v.x, v.y, v.z)).ToList();
            List<int> vInd = r.Mesh.VertexIndices().ToList();
            List<Vector3d> movedVerts = r.Mesh.Vertices().Select(v => new Vector3d(v.x, v.y, v.z)).ToList();
            for (int i = 0; i < r.Mesh.VertexCount; i++)
            {
                verts[vInd[i]] = movedVerts[i];
            }

            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(verts, r.Mesh.Triangles());
        }

        public static DMesh3 UnusedVertexRemover(DMesh3 mesh)
        {
            // Use this instead...:
            // MeshEditor.RemoveUnusedVertices(mesh);

            List<Vector3d> verts = mesh.Vertices().ToList();
            List<Index3i> tris = mesh.Triangles().ToList();
            for (int i = 0; i < verts.Count(); i++)
            {
                bool used = false;
                foreach (var tri in tris)
                {
                    for (int k = 0; k < tri.array.Length; k++)
                    {
                        if (tri[k] == i)
                        {
                            used = true;
                            break;
                        }
                    }
                    if (used)
                    {
                        break;
                    }
                }
                if (!used)
                {
                    verts.RemoveAt(i);
                    for (int j = 0; j < tris.Count(); j++)
                    {
                        var tri = tris[j];
                        for (int k = 0; k < tri.array.Length; k++)
                        {
                            if (tri[k] == i)
                            {
                                throw new Exception("HUH. I guess it wasnt unused...");
                            }
                            if (tri[k] > i)
                            {
                                tri[k] -= 1;
                            }
                        }

                        tris[j] = tri;
                    }
                    i--;
                }
            }

            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(verts, tris);
        }

        public static DMesh3 VertexAdderByTri(DMesh3 mesh)
        {
            List<Vector3d> verts = mesh.Vertices().ToList();
            List<Index3i> tris = mesh.Triangles().ToList();
            for (int i = 0; i < verts.Count(); i++)
            {
                int count = 0;
                for (int j = 0; j < tris.Count(); j++)
                {
                    Index3i tri = tris[j];
                    for (int k = 0; k < tri.array.Length; k++)
                    {
                        if (tri[k] == i)
                        {
                            if (count > 1)
                            {
                                tri[k] = verts.Count();
                                verts.Add(new Vector3d(verts[i].x, verts[i].y, verts[i].z));
                            }
                            count++;
                        }
                    }
                    tris[j] = tri;
                }
            }

            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(verts, tris);
        }

        public static DMesh3 VertexRemover(DMesh3 mesh)
        {
            List<Vector3d> verts = mesh.Vertices().ToList();
            List<Index3i> tris = mesh.Triangles().ToList();
            for (int i = 0; i < verts.Count; i++)
            {
                for (int j = verts.Count - 1; j > i; j--)
                {
                    if (verts[i].Distance(verts[j]) < double.Epsilon)
                    {
                        verts.RemoveAt(j);
                        for (int t = 0; t < tris.Count(); t++)
                        {
                            Index3i tri = tris[t];
                            for (int k = 0; k < tri.array.Length; k++)
                            {
                                if (tri[k] == j)
                                {
                                    tri[k] = i;
                                }
                                if (tri[k] > j)
                                {
                                    tri[k]--;
                                }
                            }
                            tris[t] = tri;
                        }
                    }
                }
            }

            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(verts, tris);
        }

        public static DMesh3 DuplicateTriangleRemover(DMesh3 mesh)
        {
            List<Index3i> tris = mesh.Triangles().ToList();
            for (int i = 0; i < tris.Count; i++)
            {
                for (int j = i + 1; j < tris.Count; j++)
                {
                    Index3i triI = tris[i];
                    Index3i triJ = tris[j];

                    int matchingVertexCount = 0;
                    for (int x = 0; x < triI.array.Length; x++)
                    {
                        for (int y = 0; y < triJ.array.Length; y++)
                        {
                            if (triI[x] == triJ[y])
                            {
                                matchingVertexCount++;
                            }
                        }
                    }
                    if (matchingVertexCount == 3)
                    {
                        tris.RemoveAt(j);
                        j--;
                    }
                }
            }

            return DMesh3Builder.Build<Vector3d, Index3i, Vector3d>(mesh.Vertices(), tris);
        }

        public static List<Vector3d> CalculateNormals(DMesh3 mesh)
        {
            var verts1 = mesh.Vertices().ToList();
            Vector3d[] normals = new Vector3d[verts1.Count()];
            foreach (var tri in mesh.Triangles())
            {
                Vector3d v1 = verts1[tri.a];
                Vector3d v2 = verts1[tri.b];
                Vector3d v3 = verts1[tri.c];
                Vector3d normal = Vector3d.Cross((v3 - v1), (v2 - v1));
                normal.Normalize();
                normals[tri[0]] = normal;
                normals[tri[1]] = normal;
                normals[tri[2]] = normal;
            }

            return normals.ToList();
        }

        public static DMesh3 MeshBlend(DMesh3 mesh1, DMesh3 mesh2, int blendAmount = 128)
        {
            //https://www.gradientspace.com/tutorials/category/g3sharp
            Func<DMesh3, int, BoundedImplicitFunction3d> meshToBlendImplicitF = (meshIn, numcells) => {
                double meshCellsize = meshIn.CachedBounds.MaxDim / numcells;
                MeshSignedDistanceGrid levelSet = new MeshSignedDistanceGrid(meshIn, meshCellsize);
                levelSet.ExpandBounds = meshIn.CachedBounds.Diagonal * 0.25;        // need some values outside mesh
                levelSet.ComputeMode = MeshSignedDistanceGrid.ComputeModes.FullGrid;
                levelSet.Compute();
                return new DenseGridTrilinearImplicit(levelSet.Grid, levelSet.GridOrigin, levelSet.CellSize);
            };

            DMesh3 GenerateMeshF(BoundedImplicitFunction3d root, int numcells)
            {
                MarchingCubes c = new MarchingCubes();
                c.Implicit = root;
                c.RootMode = MarchingCubes.RootfindingModes.LerpSteps;      // cube-edge convergence method
                c.RootModeSteps = 5;                                        // number of iterations
                c.Bounds = root.Bounds();
                c.CubeSize = c.Bounds.MaxDim / numcells;
                c.Bounds.Expand(3 * c.CubeSize);                            // leave a buffer of cells
                c.Generate();
                MeshNormals.QuickCompute(c.Mesh);                           // generate normals
                return c.Mesh;
                //StandardMeshWriter.WriteMesh(path, c.Mesh, WriteOptions.Defaults);   // write mesh
            }
            ;

            var meshFullImplicit1 = meshToBlendImplicitF(mesh1, 64);
            var meshFullImplicit2 = meshToBlendImplicitF(mesh2, 64);
            return GenerateMeshF(new ImplicitBlend3d() { A = meshFullImplicit1, B = meshFullImplicit2, Blend = 1.0 }, blendAmount);
        }
    }
}
