using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace TransportesGutierrez.Api.Services;

public sealed record TegRow(string Id, string? Folio, string? Cliente, string? Estado,
    string? FechaRegistro, string? Orden, string? Maquina, string? Origen, string? Destino, object?[] Cells);

public static class ModeloTegWorkbook
{
    public static readonly string[] Headers = ["FECHA TRANSPORTE","COD INT","FRA No.","FECHA FRA","REMESA TRANS No.","AUT REMESA","MTO","(PR)/(PA)","REMISION ESC 1 No.","NOMBRE ESC 1","PLACA ESC 1","REMISION ESC 2 No.","NOMBRE ESC 2","PLACA ESC 2","REMISION TECNICO","NOMBRE TECNICO","CONDUCTOR CAMABAJA","PLACA CAMABAJA","PLACA REMOLQUE","CLIENTE","DIG","PROGRAMA","OBRA","OS","FECHA SOLICITUD","ESTADO OS","PESO","TIPO DE CARGA","TIPO DE CARGA (ESCOLTA)","ORIGEN","DESTINO","VALOR FLETE","VALOR TECNICO","V/L ESC No. 1","V/L ESC No. 2","VALOR TOTAL","RNDC","LEG No","VALOR ANT PARA VIAJE","FECHA ANT","No. ANTICIPO","FECHA LEGALIZACION","VERIFICACION","NOTA"];
    private static string? Text(JsonNode? n, string key) => n?[key]?.ToString();
    private static JsonNode? One(JsonNode? n) => n is JsonArray a ? a.FirstOrDefault() : n;

    public static TegRow Project(JsonNode n)
    {
        var id = Text(n,"id") ?? throw new InvalidOperationException("Servicio sin identidad.");
        var trip = One(n["servicio_trayectos"]);
        var links = n["ordenes_escolta_items"] as JsonArray;
        if (links?.Count > 1) throw new InvalidOperationException($"El servicio {id} tiene varias órdenes vinculadas. Revise la relación antes de exportar.");
        var order = One(links?.FirstOrDefault()?["ordenes_escolta"]);
        var cells = new object?[44];
        cells[7] = Text(n,"service_type") switch { "PROPIO" => "PR", "TERCERO" => "PA", _ => null };
        cells[9] = Text(order,"nombre_escolta"); cells[10] = Text(order,"placa_escolta");
        cells[17] = Text(trip,"placa_camabaja"); cells[19] = Text(n,"empresa");
        if (decimal.TryParse(Text(trip,"peso_toneladas"), NumberStyles.Number, CultureInfo.InvariantCulture, out var weight)) cells[26] = weight;
        // La equivalencia de máquina con clasificación de carga aún no está confirmada.
        // Se conserva máquina en Trazabilidad, sin inventar AC ni documentos asociados.
        cells[29] = Text(trip,"origen"); cells[30] = Text(trip,"destino");
        cells[43] = Text(order,"observaciones");
        return new(id,Text(n,"folio"),Text(n,"empresa"),Text(n,"estado_operativo"),Text(n,"created_at"),
            Text(order,"codigo_orden") ?? Text(order,"consecutivo"),Text(trip,"maquina"),Text(trip,"origen"),Text(trip,"destino"),cells);
    }

    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static string Col(int n) { var s=""; for(n++;n>0;n=(n-1)/26) s=(char)('A'+(n-1)%26)+s; return s; }
    public static byte[] Create(IReadOnlyList<TegRow> rows, string? desde, string? hasta, string cutoff)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create,true))
        {
            Add(zip,"[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
                """);
            Add(zip,"_rels/.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(zip,"xl/workbook.xml","""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Hoja1" sheetId="1" r:id="rId1"/><sheet name="Trazabilidad" sheetId="2" r:id="rId2"/></sheets></workbook>""");
            Add(zip,"xl/_rels/workbook.xml.rels","""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Add(zip,"xl/styles.xml","""<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF0F6E56"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1" vertical="center"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
            Sheet(zip,"xl/worksheets/sheet1.xml",Headers,rows.Select(r=>r.Cells));
            var note = $"Filtro: {(desde is null ? "todos" : desde+" a "+hasta)} por fecha de registro (Colombia). Corte de altas: {cutoff}. Incluye borradores. Datos leídos durante la exportación; no es una instantánea transaccional. Vacío = sin dato comprobable, no cero. Fecha de transporte, COD INT, remisiones y campos RNDC no se infieren. Máquina se conserva aquí hasta confirmar su equivalencia con AC. No hay liquidación vinculada: importes y total quedan vacíos. NOTA contiene observación general de la orden.";
            Sheet(zip,"xl/worksheets/sheet2.xml",["FILA HOJA1","SERVICIO ID","FOLIO SERVICIO","FECHA REGISTRO UTC","ORDEN","ESTADO","MÁQUINA","CRITERIO"],
                rows.Select((r,i)=>new object?[]{i+2,r.Id,r.Folio,r.FechaRegistro,r.Orden,r.Estado,r.Maquina,i==0?note:null})
                .DefaultIfEmpty([null,null,null,null,null,null,null,note]));
        }
        return output.ToArray();
    }
    private static void Add(ZipArchive zip,string path,string xml)
    { using var s=zip.CreateEntry(path).Open(); using var w=new StreamWriter(s,new System.Text.UTF8Encoding(false)); w.Write(xml); }
    private static void Sheet(ZipArchive zip,string path,string[] headers,IEnumerable<object?[]> rows)
    {
        using var s=zip.CreateEntry(path,CompressionLevel.Fastest).Open();
        using var w=XmlWriter.Create(s,new XmlWriterSettings{Encoding=new System.Text.UTF8Encoding(false),CloseOutput=false});
        w.WriteStartDocument(); w.WriteStartElement("worksheet",Ns);
        w.WriteStartElement("sheetViews");w.WriteStartElement("sheetView");w.WriteAttributeString("workbookViewId","0");w.WriteStartElement("pane");w.WriteAttributeString("ySplit","1");w.WriteAttributeString("topLeftCell","A2");w.WriteAttributeString("activePane","bottomLeft");w.WriteAttributeString("state","frozen");w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
        w.WriteStartElement("cols");for(var i=0;i<headers.Length;i++){w.WriteStartElement("col");w.WriteAttributeString("min",(i+1).ToString());w.WriteAttributeString("max",(i+1).ToString());w.WriteAttributeString("width",i==headers.Length-1?"50":"23");w.WriteAttributeString("customWidth","1");w.WriteEndElement();}w.WriteEndElement();
        w.WriteStartElement("sheetData"); var index=1; WriteRow(headers.Cast<object?>().ToArray(),true);
        foreach(var row in rows) WriteRow(row,false);
        w.WriteEndElement();w.WriteStartElement("autoFilter");w.WriteAttributeString("ref",$"A1:{Col(headers.Length-1)}{index-1}");w.WriteEndElement();w.WriteEndElement();w.WriteEndDocument();
        void WriteRow(object?[] cells,bool header)
        {
            w.WriteStartElement("row");w.WriteAttributeString("r",index.ToString());if(header){w.WriteAttributeString("ht","42");w.WriteAttributeString("customHeight","1");}
            for(var c=0;c<cells.Length;c++)
            {
                if(cells[c] is null)continue;
                w.WriteStartElement("c");w.WriteAttributeString("r",Col(c)+index);w.WriteAttributeString("s",header?"1":"0");
                if(cells[c] is decimal or int){w.WriteElementString("v",Convert.ToString(cells[c],CultureInfo.InvariantCulture));}
                else {w.WriteAttributeString("t","inlineStr");w.WriteStartElement("is");w.WriteStartElement("t");w.WriteAttributeString("xml","space",null,"preserve");
                    var value=cells[c]!.ToString()!;
                    if(value.Length>32767) throw new ArgumentException("Un campo supera el límite de texto de Excel (32.767 caracteres). No se generó un archivo truncado.");
                    w.WriteString(new string(value.Where(XmlConvert.IsXmlChar).ToArray()));w.WriteEndElement();w.WriteEndElement();}
                w.WriteEndElement();
            }
            w.WriteEndElement();index++;
        }
    }
}
