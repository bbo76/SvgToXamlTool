using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DrawingImageToCanvas.App
{
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, LinearGradientBrush> _gradients = [];

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "SVG files (*.svg)|*.svg"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string svgContent = System.IO.File.ReadAllText(openFileDialog.FileName);
                    SvgBitmap.UriSource = new Uri(openFileDialog.FileName);
                    string xamlContent = ConvertSvgToXaml(svgContent);
                    ResultTextBox.Text = xamlContent;
                    SvgContentControl.Template = ParseXAML<ControlTemplate>(xamlContent);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Message:{ex.Message}\r\nStack:{ex.StackTrace}", caption: "Error", button: MessageBoxButton.OK, icon: MessageBoxImage.Error);
                }
            }
        }

        private string ConvertSvgToXaml(string svgContent)
        {
            XNamespace svgNs = "http://www.w3.org/2000/svg";
            XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";
            XDocument svgDoc = XDocument.Parse(svgContent);

            var svgElement = svgDoc.Root;

            if (svgElement == null)
                return string.Empty;

            XElement controlTemplate = new(xamlNs + "ControlTemplate",
                new XAttribute(xNs + "Key", "Template_Name"));    

            XElement canvas = new(xamlNs + "Canvas");
            string viewBox = svgElement.Attribute("viewBox")?.Value ?? string.Empty;

            if (!string.IsNullOrEmpty(viewBox))
            {
                var viewBoxValues = viewBox.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                canvas.SetAttributeValue("Width", viewBoxValues[2]);
                canvas.SetAttributeValue("Height", viewBoxValues[3]);
            }
            else
            {
                canvas.SetAttributeValue("Width", svgElement.Attribute("width")?.Value ?? "24");
                canvas.SetAttributeValue("Height", svgElement.Attribute("height")?.Value ?? "24");
            }

            foreach (XElement gradient in svgElement.Descendants(svgNs + "linearGradient"))
            {
                LinearGradientBrush currentGradient = new();
                string x1 = gradient.Attribute("x1")?.Value ?? "0";
                string y1 = gradient.Attribute("y1")?.Value ?? "0";
                string x2 = gradient.Attribute("x2")?.Value ?? "0";
                string y2 = gradient.Attribute("y2")?.Value ?? "0";
                currentGradient.StartPoint = new Point(double.Parse(x1.Replace("%", ""), CultureInfo.InvariantCulture) / 100, double.Parse(y1.Replace("%", ""), CultureInfo.InvariantCulture) / 100);
                currentGradient.EndPoint = new Point(double.Parse(x2.Replace("%", ""), CultureInfo.InvariantCulture) / 100, double.Parse(y2.Replace("%", ""), CultureInfo.InvariantCulture) / 100);

                foreach (var stop in gradient.Elements(svgNs + "stop"))
                {
                    var offset = stop.Attribute("offset")?.Value ?? "0";
                    var color = stop.Attribute("stop-color")?.Value ?? "#000000";
                    currentGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(color), double.Parse(offset.Replace("%", ""), CultureInfo.InvariantCulture) / 100));
                }

                _gradients[gradient.Attribute("id")?.Value ?? "0"] = currentGradient;
            }

            ConvertElement(svgElement, canvas, xamlNs);

            controlTemplate.Add(canvas);
            string result = controlTemplate.ToString(SaveOptions.DisableFormatting)
                .Replace(" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"", "")
                .Replace(" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"", "");
            return result;
        }

        private void ConvertElement(XElement svgElement, XElement parentXaml, XNamespace xamlNs)
        {
            foreach (var element in svgElement.Elements())
            {
                XElement? xamlElement = null;

                switch (element.Name.LocalName.ToLower())
                {
                    case "g":
                        xamlElement = new XElement(xamlNs + "Canvas");
                        ConvertElement(element, xamlElement, xamlNs);
                        break;
                    case "path":
                        xamlElement = new XElement(xamlNs + "Path");
                        xamlElement.SetAttributeValue("Data", element.Attribute("d")?.Value);
                        break;
                    case "rect":
                        xamlElement = new XElement(xamlNs + "Rectangle");
                        xamlElement.SetAttributeValue("Width", element.Attribute("width")?.Value);
                        xamlElement.SetAttributeValue("Height", element.Attribute("height")?.Value);
                        xamlElement.SetAttributeValue("Canvas.Left", element.Attribute("x")?.Value);
                        xamlElement.SetAttributeValue("Canvas.Top", element.Attribute("y")?.Value);
                        break;
                    case "circle":
                        xamlElement = new XElement(xamlNs + "Ellipse");
                        var r = double.Parse(element.Attribute("r")?.Value ?? "0", CultureInfo.InvariantCulture);
                        xamlElement.SetAttributeValue("Width", r * 2);
                        xamlElement.SetAttributeValue("Height", r * 2);
                        xamlElement.SetAttributeValue("Canvas.Left", double.Parse(element.Attribute("cx")?.Value ?? "0", CultureInfo.InvariantCulture) - r);
                        xamlElement.SetAttributeValue("Canvas.Top", double.Parse(element.Attribute("cy")?.Value ?? "0", CultureInfo.InvariantCulture) - r);
                        break;
                    case "polygon":
                        xamlElement = new XElement(xamlNs + "Polygon");
                        xamlElement.SetAttributeValue("Points", element.Attribute("points")?.Value);
                        break;
                }

                if (xamlElement != null)
                {
                    var transform = element.Attribute("transform")?.Value;
                    if (!string.IsNullOrEmpty(transform))
                    {
                        var xamlTransform = ConvertTransform(transform);
                        if (xamlTransform != null)
                        {
                            xamlElement.Add(new XElement(xamlNs + "Canvas.RenderTransform", xamlTransform));
                        }
                    }

                    var style = element.Attribute("style")?.Value;
                    if (!string.IsNullOrEmpty(style))
                    {
                        ApplyStyle(xamlElement, style);
                    }

                    ApplyDirectAttributes(xamlElement, element);
                    parentXaml.Add(xamlElement);
                }
            }
        }

        private static XElement? ConvertTransform(string svgTransform)
        {
            XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

            if (svgTransform.StartsWith("translate"))
            {
                var match = TranslateRegex().Match(svgTransform);
                if (match.Success)
                {
                    var x = match.Groups[1].Value;
                    var y = match.Groups[2].Success ? match.Groups[2].Value : "0";
                    return new XElement(xamlNs + "TranslateTransform",
                        new XAttribute("X", x),
                        new XAttribute("Y", y));
                }
            }

            return null;
        }

        private void ApplyStyle(XElement xamlElement, string style)
        {
            var styleProperties = style.Split(';')
                .Select(s => s.Split(':'))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim());

            foreach (var prop in styleProperties)
            {
                ApplyStyleProperty(xamlElement, prop.Key, prop.Value);
            }
        }

        private void ApplyDirectAttributes(XElement xamlElement, XElement svgElement)
        {
            var relevantAttributes = new[] { "fill", "stroke", "stroke-width", "stroke-linejoin", "stroke-linecap" };

            foreach (var attr in relevantAttributes)
            {
                var value = svgElement.Attribute(attr)?.Value;
                if (!string.IsNullOrEmpty(value))
                {
                    ApplyStyleProperty(xamlElement, attr, value);
                }
            }
        }

        private static XElement ConvertGradient(LinearGradientBrush gradient)
        {
            XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XElement xElement = new(xamlNs + "LinearGradientBrush");
            xElement.SetAttributeValue("StartPoint", gradient.StartPoint.ToString(CultureInfo.InvariantCulture));
            xElement.SetAttributeValue("EndPoint", gradient.EndPoint.ToString(CultureInfo.InvariantCulture));
            foreach (var stop in gradient.GradientStops)
            {
                XElement stopElement = new(xamlNs + "GradientStop");
                stopElement.SetAttributeValue("Color", stop.Color);
                stopElement.SetAttributeValue("Offset", stop.Offset);
                xElement.Add(stopElement);
            }
            return xElement;
        }

        private void ApplyStyleProperty(XElement xamlElement, string property, string value)
        {
            switch (property)
            {
                case "fill":
                    if (value != "none")
                    {
                        if (value.StartsWith("url(#"))
                        {
                            var gradientId = value[5..^1];
                            if (_gradients.TryGetValue(gradientId, out var gradient))
                            {
                                XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                                XElement innerFill = new(xamlNs + "Path.Fill");
                                var zozo = ConvertGradient(gradient);
                                if (zozo != null)
                                    innerFill.Add(zozo);
                                xamlElement.Add(innerFill);
                            }
                        }
                        else
                        {
                            xamlElement.SetAttributeValue("Fill", value);
                        }
                    }
                    break;
                case "stroke":
                    xamlElement.SetAttributeValue("Stroke", value);
                    break;
                case "stroke-width":
                    xamlElement.SetAttributeValue("StrokeThickness", value);
                    break;
                case "stroke-linejoin":
                    xamlElement.SetAttributeValue("StrokeLineJoin", value);
                    break;
                case "stroke-linecap":
                    xamlElement.SetAttributeValue("StrokeStartLineCap", value);
                    xamlElement.SetAttributeValue("StrokeEndLineCap", value);
                    break;
            }
        }

        public static string SerializeToXAML(object element)
        {
            return XamlWriter.Save(element);
        }

        public static T ParseXAML<T>(string xaml)
        {
            ParserContext context = new();
            context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
            context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
            return (T)XamlReader.Parse(xaml, context);
        }

        [GeneratedRegex(@"translate\(([-\d.]+),?\s*([-\d.]+)?\)")]
        private static partial Regex TranslateRegex();
    }
}