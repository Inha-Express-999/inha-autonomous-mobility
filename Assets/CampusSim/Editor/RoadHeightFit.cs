using System;

// Pure numerical core shared by the Unity baker and the standalone regression executable.
public struct RoadHeightFit
{
    double weight, y, x, z, xx, zz, xz, xy, zy;

    public void Add(double dx, double dz, double height, double sigma)
    {
        if (!(sigma > 0) || double.IsInfinity(sigma) ||
            double.IsNaN(dx) || double.IsInfinity(dx) ||
            double.IsNaN(dz) || double.IsInfinity(dz) ||
            double.IsNaN(height) || double.IsInfinity(height))
            throw new ArgumentException("Finite samples and positive finite smoothing radius required.");
        double distanceSquared=dx*dx+dz*dz, radius=3*sigma;
        double taper=Math.Max(0,1-distanceSquared/(radius*radius));
        double w=Math.Exp(-distanceSquared/(2*sigma*sigma))*taper*taper*taper*taper;
        weight+=w;y+=w*height;x+=w*dx;z+=w*dz;
        xx+=w*dx*dx;zz+=w*dz*dz;xz+=w*dx*dz;xy+=w*dx*height;zy+=w*dz*height;
    }

    public bool TryHeight(out double height)
    {
        height=0;
        if(weight<=1e-12)return false;
        double a=xx-x*x/weight,b=zz-z*z/weight,c=xz-x*z/weight;
        double u=xy-x*y/weight,v=zy-z*y/weight;
        double determinant=a*b-c*c;
        // A narrow road can have only one row of grid samples. Tiny regularization
        // preserves the observed longitudinal slope without unstable lateral extrapolation.
        if(determinant<=1e-12*(a+b)*(a+b))
        {
            double ridge=Math.Max(1e-12,(a+b)*1e-10);
            a+=ridge;b+=ridge;determinant=a*b-c*c;
        }
        height=y/weight-((u*b-v*c)*x+(v*a-u*c)*z)/(determinant*weight);
        return !double.IsNaN(height) && !double.IsInfinity(height);
    }
}
