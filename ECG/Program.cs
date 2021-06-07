using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ECG
{
    class Program
    {
        static void Main(string[] args)
        {
            ECG_Live myECG = new ECG_Live();
            myECG.Init();
        }
    }
}
