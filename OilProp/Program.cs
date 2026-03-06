using static System.Math;

// PVT Correlations Psat, Bob, Muob, cob from URTeC 3857308 by Cronin and Blasingame

Console.ForegroundColor = ConsoleColor.DarkCyan;

double T = 200;
double P = 4000;
double API = 38;
double Rs = 600;
double Rsi = 600;
double SG = 0.8;
double Tpc = 369.6;
double Ppc = 715.6;
double Z = Zfact(T, P, Tpc, Ppc);
double H2S = 0.0;
double CO2 = 0.0;
double N2 = 0.0;

double aa = Psat(T, API, Rs, SG); // URTEC 3857308
double xx = PsatERP(T, API, Rs, SG);  // URTEC 3723147 eqn 24, 8 Coefficient Exponential Rational Polynomial
double yy = PsatStand(T, API, Rs, SG);  // URTEC 3723147 eqn 4, re-fitted Standing
double pf = PsatPF(T, API, Rs, SG);  // URTEC 3723147 eqn 6, re-fitted Petrosky and Farshad




double bb = Bob (T, API, Rs, Rsi, SG, aa);
double cc = Muob(T, API, Rs, Rsi, SG, pf);
double dd = cob (T, API, Rs, Rsi, SG, aa);
double ee = Zfact(T, P, Tpc, Ppc);
double ff = cgas (T, P, Tpc, Ppc);
// Gviscy_sweet(double p, double T, double z, double SG)
double gg = Gviscy_sweet(P, T, Z, SG);
// Gviscy_acid(double p, double T, double Tpc, double Ppc, double SG, double yH2S, double yCO2, double yN2)
double hh = Gviscy_acid(P, T, Tpc, Ppc, SG, H2S, CO2, N2);

// double Gviscy_CKB(double T, double Tpc, double p, double Ppc, double SG, double yH2S, double yCO2, double yN2)
// double ii = Gviscy_CKB(150, 351.44, 9899, 639.80, 0.6931681, 0.0, 0.0, 0.158);

// static double zfactMc(double T, double P, double SG, double yH2S, double yCO2, double yN2)
double jj = zfactMc(T, P, SG, H2S, CO2, N2);
// double kk = zMc(2.35, 1.48);

Console.WriteLine("\n");
Console.WriteLine("Psat \t{0}", aa.ToString("F0"));

Console.WriteLine("\n");
Console.WriteLine("PsatELP \t{0}", xx.ToString("F0"));

Console.WriteLine("\n");
Console.WriteLine("PsatStand \t{0}", yy.ToString("F0"));

Console.WriteLine("\n");
Console.WriteLine("PsatPF \t{0}", pf.ToString("F0"));






Console.ForegroundColor = ConsoleColor.DarkGreen;
Console.WriteLine("\n");
Console.WriteLine("Bob \t{0}", bb.ToString("F6"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine("Muob \t{0}", cc.ToString("F6"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("cob \t{0}", dd.ToString("E5"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("z-factor \t{0}", ee.ToString("E7"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.DarkGreen;
Console.WriteLine("cg \t{0}", ff.ToString("E7"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine("Gviscy Sweet, {0} psia, {1} cp", P, gg.ToString("F6"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.DarkGreen;
Console.WriteLine("Gviscy Acid, {0} psia, {1} cp", P, hh.ToString("F6"));
Console.WriteLine("\n");

Console.ForegroundColor = ConsoleColor.DarkCyan;
Console.WriteLine("z-factor McCain, \t{0}", jj.ToString("E7"));
Console.WriteLine("\n");

// Console.ForegroundColor = ConsoleColor.DarkCyan;
// Console.WriteLine("z-factor McCain, \t{0}", kk.ToString("E7"));
// Console.WriteLine("\n");



// Console.ForegroundColor = ConsoleColor.DarkYellow;
// Console.WriteLine("Gviscy CKB, {0} psia, {1} cp", p, ii.ToString("F6"));
// Console.WriteLine("\n");


Console.ReadLine();


// PVT Correlations Psat, Bob, Muob, cob from URTeC 3857308 by Cronin and Blasingame

static double Psat(double T, double API, double Rsi, double SGg)    // Eqn 1 in URTEC 3857308 missing (1+...) in denominator
{

    double[] c = {0, 7.258546E-01, -4.562008E-02, 3.198814E+00, -3.994698E-01, -1.483415E-01,
                     3.550853E-01,  2.914460E+00, 4.402225E-01, -1.791551E-01,  6.955443E-01,
                    -8.172007E-01,  4.229810E-01,-5.612631E-01,  4.735904E-02,  4.746990E-01,
                    -2.515009E-01 };

    double LnPsat =  (c[1] + c[2]  * Log(T)) * (c[3] +  c[4]  * Log(API)) * (c[5]  + c[6]  * Log(Rsi)) * (c[7]  + c[8]  * Log(SGg)) /
                 (1+ (c[9] + c[10] * Log(T)) * (c[11] + c[12] * Log(API)) * (c[13] + c[14] * Log(Rsi)) * (c[15] + c[16] * Log(SGg)));

    return Exp(LnPsat);
    
}


static double PsatERP(double T, double API, double Rs, double SGg)    // Eqn 24 in URTEC 3723147
{
    double LnPsat = (9.021 - 0.119 * Log(T)) / (1 + (2.221 - 0.531 * Log(API)) * (0.144 - 1.842E-02 * Log(Rs)) * (12.802 + 8.309 * Log(SGg)));  

    return Exp(LnPsat);
}


static double PsatStand(double T, double API, double Rs, double SGg)    // Eqn 4 and 5 in URTEC 3723147
{
    Double A = 1E-6 * T - 2.6E-4 * API;

    return 386.807 * (Pow(Rs / SGg, 0.309) * Pow(10, A) - 1.776);
}



static double PsatPF(double T, double API, double Rs, double SGg)    // Eqn 6 and 7 in URTEC 3723147
{
    Double A = 0.009278 * Pow(T, 1E-6) - 1E-5 * Pow(API, 0.8287);

    return 3530.37 * (Pow(Rs, 0.1671)/Pow(SGg, 1E-6) * Pow(10, A) - 2.6568);
}








static double Bob(double T, double API, double Rs, double Rsi, double SGg, double Ps)  // URTEC 3857308, Eqn 2
{
    
    double[] c = {0, -2.015442E-01,  2.469906E-02, -3.820311E-02, -8.841732E+00, 4.446174E+00,
                     -5.847628E-01,  1.168410E+00, -4.128435E-01,  3.803488E-02, 1.129976E+00,
                      2.794113E-02, -6.051914E-02,  3.205438E+01, -5.183342E+00, 2.575210E-01 };

    double LnBob = (c[1]  + c[2]  * Log(T)   + c[3]  * Pow(Log(T),   2)) *
                   (c[4]  + c[5]  * Log(API) + c[6]  * Pow(Log(API), 2)) *
                   (c[7]  + c[8]  * Log(Rs)  + c[9]  * Pow(Log(Rsi), 2)) *
                   (c[10] + c[11] * Log(SGg) + c[12] * Pow(Log(SGg), 2)) *
                   (c[13] + c[14] * Log(Ps)  + c[15] * Pow(Log(Ps),  2));

    return Exp(LnBob);

}

static double Muob(double T, double API, double Rs, double Rsb, double SGg, double Ps)    // URTEC 3857308, Eqn 3
{

    double[] c = {0, -3.151958E+00,  1.262608E+00, -1.340409E-01,  1.547054E+00, -3.539575E-01,
                     -1.456919E+00,  1.294165E+00, -1.861708E-01, -7.171376E-03,  2.127508E-01,
                      8.056285E-02,  1.320093E-01,  4.141449E+00,  5.215495E-02, -1.750326E-02 };

    double LnMuob = (c[1]  + c[2]  * Log(T)   + c[3]  * Pow(Log(T),   2)) *
                    (c[4]  + c[5]  * Log(API) + c[6]  * Pow(Log(API), 2)) *
                    (c[7]  + c[8]  * Log(Rs)  + c[9]  * Pow(Log(Rsb), 2)) *
                    (c[10] + c[11] * Log(SGg) + c[12] * Pow(Log(SGg), 2)) *
                    (c[13] + c[14] * Log(Ps)  + c[15] * Pow(Log(Ps),  2));

    return Exp(LnMuob);

}

static double cob(double T, double API, double Rs, double Rsb, double SGg, double Ps)   // URTEC 3857308, Eqn 4
{

    double[] c = {0, 2.188505E-01, -1.386497E-01, 1.427497E-02,  8.713745E-01, -8.232235E-01,
                     1.129037E-01, -6.633979E+00, 9.500447E-01, -4.994627E-02,  1.723455E+00,
                     2.632631E-01,  2.665751E-01, 1.236350E+01,  2.958942E+00,  -7.275459E-04 };


    double Lncob = (c[1]  + c[2]  * Log(T)   + c[3]  * Pow(Log(T),   2)) *
                   (c[4]  + c[5]  * Log(API) + c[6]  * Pow(Log(API), 2)) *
                   (c[7]  + c[8]  * Log(Rs)  + c[9]  * Pow(Log(Rsb), 2)) *
                   (c[10] + c[11] * Log(SGg) + c[12] * Pow(Log(SGg), 2)) *
                   (c[13] + c[14] * Log(Ps)  + c[15] * Pow(Log(Ps),  2));

    return Exp(Lncob);

}

static double Zfact(double T, double p, double Tpc, double Ppc)
{
    // Coefficients Modified 2/21/2017 - SPE 75721. Obtained from regression using Poettmann-Carpenter "standard" database. See Eqn. (34a) in paper.
    // Utilizes Newton-Raphson Method to find Roots for z-Factor of Dranchuk and Abou-Kassem (DAK) Equation of State

    double Tr = (T + 459.67) / Tpc;
    double Pr = p / Ppc;
    double [] a = {0, 3.024696E-01, -1.046964E+00, -1.078916E-01, -7.694186E-01, 1.965439E-01,
                      6.527819E-01, -1.118884E+00,  3.951957E-01,  9.313593E-02, 8.483081E-01, 7.880011E-01 };
    double C1 = a[1] + a[2] / Tr + a[3] / Pow(Tr, 3) + a[4] / Pow(Tr, 4) + a[5] / Pow(Tr, 5);
    double c2 = a[6] + a[7] / Tr + a[8] / Pow(Tr, 2);
    double c3 = a[9] * (a[7] / Tr + a[8] / Pow(Tr, 2)); 

    // Set initial guess of z = 1.0
    double Zfact = 1.0;
    double dz = 0.1;

    // Newton-Raphson Loop
    do
    {
        double rhor = 0.27 * Pr / (Zfact * Tr);
        double c4 = a[10] * (1 + a[11] * Pow(rhor, 2)) * (Pow(rhor, 2) / Pow(Tr, 3)) * Exp(-1 * a[11] * Pow(rhor, 2));
        double Fz = Zfact - (1 + C1 * rhor + c2 * Pow(rhor, 2) - c3 * Pow(rhor, 5) + c4);
        double dFz = 1 + C1 * rhor / Zfact + 2 * c2 * Pow(rhor, 2) / Zfact - 5 * c3 * Pow(rhor, 5) / Zfact + 2 * a[10] * Pow(rhor, 2) / (Pow(Tr, 3) * Zfact) *
                     (1 + a[11] * Pow(rhor, 2) - Pow(a[11] * Pow(rhor, 2), 2)) * Exp(-1 * a[11] * Pow(rhor, 2));
        dz = -1 * Fz / dFz;
        Zfact = Zfact + dz;
    } while (Abs(dz) >= 0.00000000001);

    return Zfact;
}

static double cgas(double T, double p, double Tpc, double Ppc)
{
   //  Coefficients Modified 2/21/2017 - SPE 75721.  Obtained from regression using Poettmann-Carpenter "standard" database.   See Eqn. (34a) in paper.
   //  Utilizes Newton-Raphson Method to find Roots for z-Factor of Dranchuk and '  Abou - Kassem(DAK) Equation of State

    double Tr = (T + 459.67) / Tpc;
    double Pr = p / Ppc;
    double z = Zfact(T, p, Tpc, Ppc);
    double rhor = 0.27 * Pr / (z * Tr);

    // Calculate partial derivative dz/drhor -- see RE Handbook (Ahmed), eqn. 2-51, p.65
    
    double [] b = { 0, 3.1506237E-01, -1.0467099E+00, -5.783272E-01, 5.3530771E-01, -6.1232032E-01,
                      -1.0488813E-01,  6.8157001E-01,  6.8446549E-01 };

    double t1 = b[1] + b[2] / Tr + b[3] / Pow(Tr, 3);
    double t2 = b[4] + b[5] / Tr;
    double t3 = b[5] * b[6] / Tr;
    double t4 = b[7] / Pow(Tr, 3);
    double t5 = 0.27 * Pr / Tr;

    double dz_drhor = t1 + 2 * t2 * rhor + 5 * t3 * Pow(rhor, 4) + 2 * t4 * rhor * (1 + b[8] * Pow(rhor, 2) - Pow(b[8], 2) * Pow(rhor, 4)) * Exp(-b[8] * Pow(rhor, 2));

    // Calculate cpr, eqn. 2-50 p. 64 in RE Handbook (Ahmed)
    double cpr = 1 / Pr - (0.27 / (Pow(z, 2) * Tr)) * (dz_drhor / (1 + (rhor / z) * dz_drhor));

    // Calculate cg, eqn. 2-48, p. 61 in RE Handbook (Ahmed)
    return cpr / Ppc;
}

static double Gviscy_sweet(double p, double T, double z, double SG)
{

    // Gas viscosity calculated using Lee-Gonzalez-Eakin, for sweet gases/
    // Correlation good up to 8000 psia and 100-340F.   Can also be used for gases containing CO2 up to 0.032 mole fraction. 
    // Modified 2/21/2017 -- See SPE 75721 Eqn. (20) for optimized coefficients -- average abs error ~ 2.29 (2-4 for original model).
    // Std Dev. 2.69%, max 9% compared to experimental data
    // Valid for 100 < p(psia) < 8000,   100<T(F) < 340

    double M = 28.963 * SG;
    double rho = 0.0014935 * p * M / (z * (T + 459.67));

    double [] k = { 0, 1.67175E+01, 4.19188E-02, 1.40256E+00, 2.12209E+02, 1.81349E+01 };

    double [] x = { 0, 2.12574E+00, 2.06371E+03, 1.1926E-02 };

    double [] y = { 0, 1.09809E+00, -3.92851E-02 };
    
    double kk = ((k[1] + k[2] * M) * Pow(T + 460, k[3])) / (k[4] + k[5] * M + (T + 460));
    double xx = x[1] + x[2] / (T + 460) + x[3] * M;
    double yy = y[1] - y[2] * xx;

    return 0.0001 * kk * Exp(xx * Pow(rho, yy));
}


static double Gviscy_acid(double p, double T, double Tpc, double Ppc, double SG, double yH2S, double yCO2, double yN2)
{
    // Gas viscosity calculated using Carr-Kobayashi-Burrows -- for acid gases
    // Assume this has same CKB limits:  Up to 400F, Pr < 20 (~ 12,000 psia), Tr < 3.0.   Handles CO2, N2, and H2S up to 15% each.
    // See SPE 106326 (2005) - Gawish

    // First.....
    // Calculate viscosity @ 1 atm and reservoir temp. using Standing expression
    double u1_uncorr = (0.00001709 - 0.000002062 * SG) * T + 0.008188 - 0.00615 * Log10(SG);
    double uCO2 = yCO2 * (9.08 * Log10(SG) + 6.24) / 1000;
    double uN2 = yN2 * (8.48 * Log10(SG) + 9.59) / 1000;
    double uH2S = yH2S * (8.49 * Log10(SG) + 3.73) / 1000;
    double u1 = u1_uncorr + uCO2 + uN2 + uH2S;

    // Use Dempsey expression for viscosity ratio ug/u1
    double Tpr = (T + 459.67) / Tpc;
    double Ppr = p / Ppc;

    double a0 = -2.4621182;
    double a1 = 2.970547414;
    double a2 = -0.286264054;
    double a3 = 0.00805420522;
    double a4 = 2.80860949;
    double a5 = -3.49803305;
    double a6 = 0.36037302;
    double a7 = -0.01044324;
    double a8 = -0.793385648;
    double a9 = 1.39643306;
    double a10 = -0.149144925;
    double a11 = 0.00441015512;
    double a12 = 0.0839387178;
    double a13 = -0.18648848;
    double a14 = 0.0203367881;
    double a15 = -0.000609579263;

    // Eqn. 23
    return u1 * Exp(a0 + a1 * Ppr + a2 * Pow(Ppr, 2) + a3 * Pow(Ppr, 3) + 
                         Tpr * (a4 + a5 * Ppr + a6 * Pow(Ppr, 2) + a7 * Pow(Ppr, 3)) +
                         Pow(Tpr, 2) * (a8 + a9 * Ppr + a10 * Pow(Ppr, 2) + a11 * Pow(Ppr, 3)) + 
                         Pow(Tpr, 3) * (a12 + a13 * Ppr + a14 * Pow(Ppr, 2) + a15 * Pow(Ppr, 3)) - 
                         Log(Tpr));
}


// 2011 MCCAIN PVT ***************************************************************************************************************************************
// 2011 MCCAIN PVT ***************************************************************************************************************************************

static double zfactMc(double T, double P, double SG, double yH2S, double yCO2, double yN2)
{
    double[] alpha = { 1.1582E-01, -4.5820E-01, -9.0348E-01, -6.6026E-01, 7.0729E-01, -9.9397E-02 };
    double[] beta = { 3.8216E+00, -6.5340E-02, -4.2113E-01, -9.1249E-01, 1.7438E+01, -3.2191E+00 };

    // for J and K, summations are H2S, CO2, and N2 in that order

    double J = alpha[0] +
               alpha[1] * yH2S * Constants.TcH2S / Constants.PcH2S +
               alpha[2] * yCO2 * Constants.TcCO2 / Constants.PcCO2 +
               alpha[3] * yN2 * Constants.TcN2 / Constants.PcN2 +
               alpha[4] * SG + alpha[5] * Pow(SG, 2);

    double K = beta[0] +
               beta[1] * yH2S * Constants.TcH2S / Math.Sqrt(Constants.PcH2S) +
               beta[2] * yCO2 * Constants.TcCO2 / Math.Sqrt(Constants.PcCO2) +
               beta[3] * yN2 * Constants.TcN2 / Math.Sqrt(Constants.PcN2) +
               beta[4] * SG + beta[5] * Pow(SG, 2);

    double Tpc = Pow(K, 2) / J;
    double Ppc = Tpc / J;
    Console.WriteLine("Tpc {0}", Tpc.ToString("F2"));
    Console.WriteLine("Ppc {0}", Ppc.ToString("F2"));

    double Tpr = (T + 459.67) / Tpc;
    double Ppr = P / Ppc;

    // ******************************************************************************************************
    // Loop
    // ******************************************************************************************************

    // for initial guess, assume z = 1.0
    double rhopr = 0.27 * Ppr / Tpr;
    double z = zMc(Tpr, rhopr);
    double delta = z - 1;
    double zguess = 0;
    int n = 0;  

    if (Ppr <= 2.0)
    {
        do
        {
            zguess = z + delta;
            rhopr = 0.27 * Ppr / (zguess * Tpr);
            z = zMc(Tpr, rhopr);
            delta = z - zguess;
            n++;
            Console.WriteLine("number of iterations: {0}", n.ToString("F0"));

        } while (Abs(delta) > 0.001);
    }

    if (Ppr > 2.0 && Ppr <= 3.0)
    {
        do
        {
            zguess = z + delta / 2.0;
            rhopr = 0.27 * Ppr / (zguess * Tpr);
            z = zMc(Tpr, rhopr);
            delta = z - zguess;
            n++;
            Console.WriteLine("number of iterations: {0}", n.ToString("F0"));

        } while (Abs(delta) > 0.001);
    }

    if (Ppr > 3.0 && Ppr <= 6.0)
    {
        do
        {
            zguess = z + delta / 3.0;
            rhopr = 0.27 * Ppr / (zguess * Tpr);
            z = zMc(Tpr, rhopr);
            delta = z - zguess;
            n++;
            Console.WriteLine("number of iterations: {0}", n.ToString("F0"));

        } while (Abs(delta) > 0.001);
    }

    if (Ppr > 6.0)
    {
        do
        {
            zguess = z + delta / 5.0;
            rhopr = 0.27 * Ppr / (zguess * Tpr);
            z = zMc(Tpr, rhopr);
            delta = z - zguess;
            n++;
            Console.WriteLine("number of iterations: {0}", n.ToString("F0"));

        } while (Abs(delta) > 0.001);
    }

    else
    {
        return 0;
    }

    return z;


}
static double zMc(double Tpr, double rhopr)
{
    double[] A = { 0, 0.3265, -1.07, -0.5339, 0.01569, -0.05165, 0.5475, -0.7361, 0.1844, 0.1056, 0.6134, 0.7210 };
    return 1 + (A[1] + A[2] / Tpr + A[3] / Pow(Tpr, 3) + A[4] / Pow(Tpr, 4) + A[5] / Pow(Tpr, 5)) * rhopr
             + (A[6] + A[7] / Tpr + A[8] / Pow(Tpr, 2)) * Pow(rhopr, 2)
             - A[9] * (A[7] / Tpr + A[8] / Pow(Tpr, 2)) * Pow(rhopr, 5) 
             + A[10] * (1 + A[11] * Pow(rhopr, 2)) * (Pow(rhopr, 2) / Pow(Tpr, 3)) * Exp(-A[11] * Pow(rhopr, 2));

}
public static class Constants
{
    public const double TcH2S = 671.97;  //deg R from Engineering Toolbox
    public const double TcCO2 = 547.43;  //deg R from Engineering Toolbox
    public const double TcN2 = 227.07;   //deg R from Engineering Toolbox
    public const double PcH2S = 1301;    //psia from Engineering Toolbox
    public const double PcCO2 = 1070;    //psia from Engineering Toolbox
    public const double PcN2 = 493.0;    //psia from Engineering Toolbox
}



/*
 
// Get CS1065 unassigned variable error for Vtemp at Label50 in code -- can not figure out.   Code works in VBA but not C#.

 
double Gviscy_CKB(double T, double Tpc, double p, double Ppc, double SG, double yH2S, double yCO2, double yN2)
{
    // Gas viscosity using Carr-Kobayashi-Burrows.
    // Uses Dranchuk, othogonal polynomial curve fits -- see JCPT 86-01-03 -- SEE PROGRAM IN APPENDIX
    // Use up to 400F, Pr < 20 (~ 12,000 psia), Tr < 3.0.   Handles CO2, N2, and H2S up to 15% each.
    // Created January 2019

    double[] A = new double[15];
    A[0] = 0.0016846;
    A[1] = -0.0548393;
    A[2] = 0.0320827;
    A[3] = 0.375024;
    A[4] = -0.0518907;
    A[5] = -0.142471;
    A[6] = -0.773172;
    A[7] = 0.558395;
    A[8] = -0.427826;
    A[9] = 0.411381;
    A[10] = 0.463066;
    A[11] = -0.675948;
    A[12] = 0.971044;
    A[13] = -0.832204;
    A[14] = 0.17138;

    double[] B = new double[21];
    B[0] = 0.174456;
    B[1] = -2.0844803;
    B[2] = 1.1683397;
    B[3] = 10.9273005;
    B[4] = -9.5398703;
    B[5] = 4.4983101;
    B[6] = -24.2991028;
    B[7] = 19.5294037;
    B[8] = -0.46506;
    B[9] = -8.6797705;
    B[10] = 23.6387024;
    B[11] = -13.8533001;
    B[12] = -11.0101004;
    B[13] = 8.9948301;
    B[14] = 6.4956799;
    B[15] = -8.3443699;
    B[16] = 2.4460001;
    B[17] = 9.3275099;
    B[18] = -7.1545696;
    B[19] = 1.8098602;
    B[20] = -3.3509197;

    double[] C = new double[10];
    C[0] = 0.468794;
    C[1] = -1.3927296;
    C[2] = 1.1024399;
    C[3] = -0.358802;
    C[4] = 2.3297396;
    C[5] = -1.2382603;
    C[6] = 0.155171;
    C[7] = 0.255111;
    C[8] = -1.5413198;
    C[9] = 0.749261;

    double[] D = new double[6];
    D[0] = 1.443;
    D[1] = 1.639;
    D[2] = 52.2;
    D[3] = -105.019;
    D[4] = 131.7;
    D[5] = 32.29;

label10:
    int IN_ = 0;
    double Tr = (T + 459.67) / Tpc;
    double Ppr = p / Ppc;
    double h = Log(Tr);
    double Plimit = D[0] + D[1] * h + D[2] * Pow(h, 2) + D[3] * Pow(h, 3) + D[4] * Pow(h, 4) + D[5] * Pow(h, 5);

    // If Tr > 2 use Function 1 for Pr upto 3.
    if (Tr >= 2 && Ppr <= 3) goto label60;

    // If Tr>1.2 use Function 1 for Pr upto 2.
    if (Tr >= 1.2 && Ppr <= 2) goto label60;

    // If Tr < 1.19 and 0.85 <Pr<1.01 use average of Function 1 and 2.
    if (Tr >= 1.19) goto label20;
    if (Ppr >= 0.85 && Ppr <= 1.01) IN_ = 1;
    if (Ppr <= 1.01) goto label60;
    goto label30;

    // If 1.19 < Tr < 1.2 use average of function 1 and 2 for 0.85 < Pr < 1.07
label20:
    if (Tr > 1.2) goto label30;
    if (Ppr >= 0.85 && Ppr <= 1.07) IN_ = 1;
label30:
    if (Ppr <= 1) goto label60;

    // If Tr > 1.8 use Function 2
    if (Tr > 1.8) goto label40;

    // If Pr > Plimit use function 3
    if (Ppr > Plimit) goto label70;

    // If Ppr is in the transition zone of function 2 and 3, use average of function 2 and 3
    if (Abs(Plimit - Ppr) <= 2) IN_ = 2;

// Function 2 
label40:
    double xa = (Log10(Tr) - 0.02118938) / 0.4559317;
    double ya = Log10(Ppr) / 1.301029;
    double Visrat = Pow(10, VPT(B, 5, xa, ya));
    if (IN_ == 1) goto label50;
    if (IN_ != 2) goto label80;
    double Vtemp = Visrat;  
    goto label70;
label50:
    Visrat = (Vtemp + Visrat) / 2;
    goto label80;

// Function 1
label60:
    xa = (Log10(Tr) - 0.02118938) / 0.4559317;
    ya = (Log10(Ppr) + 1) / 1.47712;
    Visrat = Pow(10, VPT(A, 4, xa, ya));
    if (IN_ != 1) goto label80;
    Vtemp = Visrat;
    IN_ = 1;
    goto label40;

// Function 3
label70:
    xa = (Log10(Tr) - 0.02118938) / 0.2218487;
    ya = (Log10(Ppr) - 0.20412) / 1.096909;
    Visrat = Pow(10, VPT(C, 3, xa, ya));
    if (IN_ == 2) Visrat = (Vtemp + Visrat) / 2;  

label80:
    double F1 = (0.00001709 - 0.000002062 * SG) * T + 0.008188 - 0.00615 * Log10(SG);
    double FCO2 = yCO2 * (9.08 * Log10(SG) + 6.24) / 1000;
    double FN2 = yN2 * (8.48 * Log10(SG) + 9.59) / 1000;
    double FH2S = yH2S * (8.49 * Log10(SG) + 3.73) / 1000;

    double VIS1 = F1 + FN2 + FCO2 + FH2S;
    return VIS1 * Visrat;
}

static double VPT(double[] C1, int IORD, double xa, double ya)
{
    // VPT Function for Carr-Kobayashi-Burrows Subroutine Gviscy_CKB
    // Function adapted from Fortran coded function VPT in Dranchuk (othogonal polynomial curve fit paper) -- see JCPT 86-01-03

    double VPT = C1[0];
    int JJ = 1;
    for (int II = 0; II < IORD; II++)
    {
        int NX = II;
        int I1 = II + 1;
        for (int NY1 = 0; NY1 < I1; NY1++)
        {
            int NY = NY1;
            double XT = 1.0;
            if (NX != 0)
            {
                XT = Pow(xa, NX);
            }
            double YT = 1.0;
            if (NY != 0)
            {
                YT = Pow(ya, NY);
            }
            VPT += C1[JJ] * XT * YT;
            NX--;
            JJ++;
        }
    }

    return VPT;
}

*/





