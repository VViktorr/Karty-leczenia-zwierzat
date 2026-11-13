namespace KartyLeczenia;

public static class AdministrationRouteLegend
{
    public const string Description = "IM – domięśniowo, IV – dożylnie, SC – podskórnie, ID – śródskórnie, IP – dootrzewnowo, IA – dostawowo, PO – doustnie, IUT – domacicznie, IMM – dowymieniowo, IVAG – dopochwowo, TOP / EXT – miejscowo / zewnętrznie, POUR-ON – przez polewanie, SPOT-ON – punktowo na skórę, OPH – do oka, OT / AUR – do ucha, PR – doodbytniczo.";

    public const string Html = $"""
        <p style="font-size:8pt;line-height:1.2;margin:1.5mm 0;break-inside:avoid"><strong>Drogi podania</strong> · {Description}</p>
        """;
}
