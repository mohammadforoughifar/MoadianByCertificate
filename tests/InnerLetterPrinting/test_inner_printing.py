"""Offline asset and source-contract checks, not a substitute for the .NET PDF render tests."""
import base64
import hashlib
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "src" / "Inventory.Api"
CLIENT = ROOT / "src" / "Inventory.Client"
OFFICE = API / "Services" / "Office"


class InnerPrintingTests(unittest.TestCase):
    def test_company_1003_has_independent_a4_a5_artwork_only_templates(self):
        for size in ("A4", "A5"):
            with self.subTest(size=size):
                template = API / "Reports/InnerLetters/Companies/1003" / f"Inner-{size}.mrt"
                root = ET.parse(template).getroot()
                self.assertEqual(root.findtext("./Pages/Page1/PaperSize"), size)
                self.assertEqual(root.find("./Pages/Page1/Components").get("count"), "0")
                self.assertEqual(root.find("./Dictionary/Databases").get("count"), "0")
                self.assertEqual(root.find("./Dictionary/DataSources").get("count"), "0")
                value = root.findtext(".//Watermark/ImageBytes")
                image = base64.b64decode("".join(value.split()), validate=True)
                self.assertTrue(image.startswith(b"\xff\xd8") and image.endswith(b"\xff\xd9"))
                approved = ET.parse(API / "Reports/OutgoingLetters/Companies/1003" / f"Outgoing-{size}.mrt")
                original = base64.b64decode(approved.findtext(".//Watermark/ImageBytes"))
                self.assertEqual(hashlib.sha256(image).digest(), hashlib.sha256(original).digest())
                self.assertNotIn("ConnectionString", template.read_text())

    def test_background_is_a_repeating_page_background_and_body_can_flow(self):
        source = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("page.Background().Image(homepaBackground!).FitUnproportionally();", source)
        self.assertIn("page.Size(a5 ? PageSizes.A5 : PageSizes.A4)", source)
        self.assertIn("c.Item().Text(body).AlignRight();", source)
        self.assertNotIn(".ShowEntire(", source)
        self.assertNotIn(".ClampLines(", source)
        self.assertNotIn(".Footer()", source)
        for generated in ('Text("با تشکر")', 'Text("مهر")', 'Text("امضاکنندگان")', 'CurrentPageNumber()'):
            self.assertNotIn(generated, source)

    def test_hamesh_is_optional_end_to_end_and_excludes_private_answers(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn("if (withHamesh)", service)
        self.assertIn('x.Type == "هامش"', service)
        self.assertNotIn(".Answer", service)
        self.assertNotIn(".Take(", service)
        renderer = (OFFICE / "InnerLetterPdfRenderer.cs").read_text()
        self.assertIn("if (withHamesh && data.Hamesh.Count > 0)", renderer)
        self.assertIn('HomepaLetterheadLayout.Attachment(a5), attachment ? "دارد" : "ندارد", false', renderer)
        self.assertIn(".ScaleToFit().Text(value)", renderer)
        controller = (API / "Controllers/Office/InnerLettersController.cs").read_text()
        self.assertIn("[FromQuery] bool withHamesh = false", controller)
        self.assertIn("HttpContext.RequestAborted, withHamesh", controller)
        client = (CLIENT / "Services/LetterServices.cs").read_text()
        self.assertIn("&withHamesh=", client)
        self.assertIn("await _api.GetFileAsync(path)", client)

    def test_letter_and_company_access_are_preserved(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        self.assertIn("x.ReciverUserId == userId", service)
        self.assertIn("letter.CreatorUserId != userId", service)
        self.assertIn("ActiveCompanyMiddleware.ItemKey", service)
        self.assertIn("x.UserId == userId && x.Company.IsActive", service)
        transport = (CLIENT / "Services/ApiClient.cs").read_text()
        method = transport.split("public async Task<(byte[] Data, string FileName, string ContentType)> GetFileAsync", 1)[1]
        method = method.split("public async Task<T> PostFileAsync", 1)[0]
        self.assertIn("AddActiveCompanyHeaderAsync(req)", method)
        self.assertIn("AddAuth(req)", method)

    def test_homepa_fails_instead_of_using_another_company_background(self):
        service = (OFFICE / "InnerLetterPrintService.cs").read_text()
        branch = service.split("if (company?.Id == 1003)", 1)[1].split("var header = company?.LetterheadFileName;", 1)[0]
        self.assertIn("InnerLetterheadTemplates.LoadHomepa", branch)
        self.assertIn("throw new InnerLetterPrintException", branch)
        self.assertNotIn("forough", branch)
        loader = (OFFICE / "InnerLetterheadTemplates.cs").read_text()
        self.assertIn('"Reports", "InnerLetters", "Companies", "1003"', loader)
        self.assertIn('Inner-{(a5 ? "A5" : "A4")}.mrt', loader)

    def test_ui_asks_for_variant_after_either_size_click(self):
        ui = (CLIENT / "Pages/Office/Letters/LetterCartable.razor").read_text()
        self.assertIn('OpenPrintOptions("A4")', ui)
        self.assertIn('OpenPrintOptions("A5")', ui)
        self.assertIn("PrintPdf(printSize, false)", ui)
        self.assertIn("PrintPdf(printSize, true)", ui)
        self.assertIn("بدون هامش", ui)
        self.assertIn("با هامش", ui)
        method = ui.split("private async Task PrintPdf(string size, bool withHamesh)", 1)[1]
        self.assertLess(method.index('"ltxPrintBegin"'), method.index("LetterSvc.GetPrintPdfAsync"))
        self.assertIn("GetPrintPdfAsync(letterId, size, withHamesh)", method)

    def test_publish_rule_includes_inner_templates(self):
        project = ET.parse(API / "Inventory.Api.csproj")
        rule = next(c for c in project.iter("Content") if c.get("Include") == "Reports/**/*.mrt")
        self.assertEqual(rule.get("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(rule.get("CopyToPublishDirectory"), "PreserveNewest")


if __name__ == "__main__":
    unittest.main()
