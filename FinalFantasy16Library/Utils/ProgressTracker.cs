using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FinalFantasy16Library.Utils
{
    public class ProgressTracker
    {
        public Action<float, string> SetProgress = (float value, string task) => { };
    }
}
