using System;

static class Program
{
    static int checks;
    static void Check(bool condition,string name)
    {
        if(!condition)throw new Exception(name);
        checks++;Console.WriteLine("PASS "+name);
    }
    static double Fit(double queryX,double queryZ,Func<double,double,double> surface,int halfWidth=4)
    {
        var fit=new RoadHeightFit();
        for(int z=-40;z<=40;z+=2)for(int x=-halfWidth;x<=halfWidth;x+=2)
            fit.Add(x-queryX,z-queryZ,surface(x,z),8);
        if(!fit.TryHeight(out double height))throw new Exception("Missing support");
        return height;
    }
    static void Main()
    {
        Check(Math.Abs(Fit(1.7,3.2,(x,z)=>5)-5)<1e-10,"constant road height");
        Check(Math.Abs(Fit(3.5,32.1,(x,z)=>5+.03*x+.08*z)-(5+.03*3.5+.08*32.1))<1e-9,
            "planar slope at asymmetric road boundary");
        Check(Math.Abs(Fit(0,32.1,(x,z)=>5+.08*z,0)-(5+.08*32.1))<1e-7,
            "one-row narrow road retains longitudinal slope");
        double residual=0;
        for(int i=0;i<80;i++)
        {
            double z=i*.1;
            residual=Math.Max(residual,Math.Abs(Fit(0,z,(x,t)=>.04*t+.2*Math.Cos(Math.PI*t/2))-.04*z));
        }
        Check(residual<.02,"4m wavelength ripple attenuated by at least 90 percent");
        // The old hard window switched samples at z=0 and produced steps on sliver triangles.
        double left=Fit(0,-.00001,(x,z)=>.01*z+.0001*z*z*z),right=Fit(0,.00001,(x,z)=>.01*z+.0001*z*z*z);
        Check(Math.Abs(right-left)<1e-6,"continuous height across sample-window boundary");
        var empty=new RoadHeightFit();Check(!empty.TryHeight(out _),"no support fails explicitly");
        var outside=new RoadHeightFit();outside.Add(100,0,5,8);Check(!outside.TryHeight(out _),"outside-radius sample excluded");
        bool rejected=false;try {empty.Add(0,0,double.NaN,8);}catch(ArgumentException){rejected=true;}
        Check(rejected,"invalid input rejected");
        Console.WriteLine($"PASS total={checks}; rippleResidualM={residual:R}; boundaryStepM={Math.Abs(right-left):R}");
    }
}
