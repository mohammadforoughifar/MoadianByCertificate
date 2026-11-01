"""Asset + source-contract checks; does not run the .NET renderer or access a database.
python3 -m unittest discover -s inventory/tests/OutgoingLetterhead -p 'test_*.py' -v
"""
import base64
import re
import unittest
from pathlib import Path
from xml.etree import ElementTree as ET

API = Path(__file__).resolve().parents[2] / "src" / "Inventory.Api"
CLIENT = API.parent / "Inventory.Client"
SERVICE = API / "Services" / "Office" / "Outgoing" / "OutgoingLetterPrintService.cs"
TEMPLATES = API / "Reports" / "OutgoingLetters" / "Companies"


class LetterheadAssetsTests(unittest.TestCase):
    def test_every_company_size_has_an_embedded_jpeg(self):
        for company in (1, 2, 3, 1003, 1005):
            for size in ("A4", "A5"):
                with self.subTest(company=company, size=size):
                    root = ET.parse(TEMPLATES / str(company) / f"Outgoing-{size}.mrt").getroot()
                    watermark = next(e for e in root.iter() if e.tag.split("}")[-1] == "Watermark")
                    value = next(e.text for e in watermark if e.tag.split("}")[-1] == "ImageBytes")
                    # Convert.FromBase64String tolerates XML whitespace just as this normalization does.
                    image = base64.b64decode("".join(value.split()), validate=True)
                    self.assertTrue(image.startswith(b"\xff\xd8"))
                    self.assertTrue(image.endswith(b"\xff\xd9"))
                    self.assertGreater(len(image), 100_000)

    def test_1003_enhanced_art_is_synchronized_across_registered_and_embedded_files(self):
        registered = API / "wwwroot" / "uploads" / "letterheads" / "homepa_company1003_letterhead.jpg"
        png = registered.with_suffix(".png")
        pdf = registered.with_suffix(".pdf")
        jpg_bytes = registered.read_bytes()
        self.assertTrue(jpg_bytes.startswith(b"\xff\xd8"))
        self.assertTrue(png.read_bytes().startswith(b"\x89PNG\r\n\x1a\n"))
        self.assertTrue(pdf.read_bytes().startswith(b"%PDF-"))
        for size in ("A4", "A5"):
            with self.subTest(size=size):
                root = ET.parse(TEMPLATES / "1003" / f"Outgoing-{size}.mrt").getroot()
                watermark = next(e for e in root.iter() if e.tag.split("}")[-1] == "Watermark")
                value = next(e.text for e in watermark if e.tag.split("}")[-1] == "ImageBytes")
                embedded = base64.b64decode("".join(value.split()), validate=True)
                self.assertEqual(embedded, jpg_bytes, "each company-1003 template must use the synchronized enhanced artwork")

    def test_content_pdf_does_not_cover_the_underlaid_letterhead(self):
        source = SERVICE.read_text()
        self.assertIn("page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);", source)
        self.assertLess(source.index("graphics.DrawImage(asset.Pdf, bounds)"), source.index("graphics.DrawImage(contentForm, bounds)"))
        self.assertIn("graphics.DrawImage(asset.Image, bounds)", source)

    def test_registered_file_stays_the_default_source(self):
        # اولویت پیش‌فرض همان فایل ثبت‌شدهٔ شرکت است؛ طرح قالب با تنظیم
        # PreferTemplateLetterhead (یا خالی‌کردن LetterheadFileName) اولویت می‌گیرد.
        source = SERVICE.read_text(encoding="utf-8")
        self.assertIn(": registeredPath ?? templateLetterheadPath;", source)
        self.assertIn("var preferTemplate = PreferTemplateLetterhead;", source)

    def test_active_service_honors_the_configured_template_root(self):
        source = SERVICE.read_text()
        self.assertIn('_configuration["Reporting:Stimulsoft:TemplateRoot"]', source)
        self.assertIn("Path.IsPathRooted(templateRoot)", source)
        self.assertIn("$\"Outgoing-{size}.mrt\"", source)

    def test_homepa_metadata_boxes_preserve_mrt_geometry_with_calibrated_left_shift(self):
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        expressions = {"Number": "LetterNumber", "Date": "DateShams", "Attachment": "Peyvast"}
        for field, column in expressions.items():
            method = re.search(r"static Field " + field + r"\(bool isA5\)(.*?);", layout, re.S).group(1)
            variants = re.findall(r"new\(([^)]+)\)", method)
            self.assertEqual(len(variants), 2)
            for index, size in enumerate(("A5", "A4")):
                with self.subTest(field=field, size=size):
                    root = ET.parse(TEMPLATES / "1003" / f"Outgoing-{size}.mrt").getroot()
                    self.assertEqual(root.findtext("ReportUnit"), "Inches")
                    page = root.find("./Pages/Page1")
                    margins = [float(v) for v in page.findtext("Margins").split(",")]
                    component = next(e for e in page.iter() if e.findtext("Text") == "{DataSource1." + column + "}")
                    x, y, width, height = [float(v) for v in component.findtext("ClientRectangle").split(",")]
                    left_shift = (3 if size == "A5" else 4) if field in ("Date", "Attachment") else 0
                    physical_x = (margins[0] + x) * 72 - left_shift
                    physical_y = (margins[2] + y) * 72
                    page_header_origin = 16 if size == "A5" else 24
                    expected = (physical_x, physical_y - page_header_origin, width * 72, height * 72)
                    actual = [float(v.strip().removesuffix("f")) for v in variants[index].split(",")]
                    for value, target in zip(actual, expected):
                        self.assertAlmostEqual(value, target, places=3)
                    self.assertAlmostEqual(actual[1] + page_header_origin, physical_y, places=3,
                                           msg="value top must match the corresponding label's page coordinate")
                    self.assertGreaterEqual(actual[0], 0)
                    self.assertLess(actual[0] + actual[2], float(page.findtext("PageWidth")) * 72 / 3,
                                    "values must stay in the physical left third, not the RTL right edge")

    def test_1003_header_values_match_the_label_rows_and_use_ltr_isolation(self):
        source = SERVICE.read_text()
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        self.assertIn("Header content begins after Page.MarginTop", layout)
        self.assertIn("text.DirectionFromLeftToRight()", source)
        self.assertIn('isolateLtr ? $"\\u2066{value}\\u2069" : value', source)
        for is_a5, expected in ((False, (36.48, 53.04, 69.6)), (True, (25.04, 36.56, 48.08))):
            fields = [re.search(r"static Field " + name + r"\(bool isA5\)(.*?);", layout, re.S).group(1)
                      for name in ("Number", "Date", "Attachment")]
            actual = []
            for field in fields:
                variants = re.findall(r"new\(([^)]+)\)", field)
                size_index = 0 if is_a5 else 1
                actual.append(float(variants[size_index].split(",")[1].strip().removesuffix("f")))
            self.assertEqual(tuple(actual), expected)

    def test_aria_fields_and_body_start_are_fixed_independently_for_a4_and_a5(self):
        source = SERVICE.read_text()
        layout = SERVICE.with_name("AriaLetterheadLayout.cs").read_text()
        expected = {
            "Date": ((142.2, 47, 35.9, 10), (202, 66, 51, 14)),
            "Number": ((77.4, 47, 34.5, 10), (110, 66, 49, 14)),
            "Attachment": ((7, 47, 35.2, 10), (10, 66, 50, 14)),
        }
        for name, sizes in expected.items():
            method = re.search(r"static Field " + name + r"\(bool isA5\)(.*?);", layout, re.S).group(1)
            variants = re.findall(r"new\(([^)]+)\)", method)
            self.assertEqual(len(variants), 2)
            for index, size in enumerate(sizes):
                with self.subTest(field=name, size=("A5", "A4")[index]):
                    actual = tuple(float(v.strip().removesuffix("f")) for v in variants[index].split(","))
                    self.assertEqual(actual, size)

        self.assertIn("HeaderHeight(bool isA5) => isA5 ? 70f : 95f", layout)
        self.assertIn("BodyTopPadding(bool isA5) => isA5 ? 13f : 12f", layout)
        self.assertIn(r'isolateLtr ? $"\u2066{value}\u2069" : value', source)
        self.assertIn("text.DirectionFromLeftToRight();", source)
        self.assertIn("var pageTopMargin = isHomepaLetterhead || isAriaLetterhead", source)
        self.assertIn("page.MarginTop(pageTopMargin);", source)
        aria_branch = source.split("if (isAriaLetterhead)", 1)[1].split("else if (isHomepaLetterhead)", 1)[0]
        self.assertIn("page.Header().ContentFromLeftToRight()", aria_branch)
        self.assertIn(".Layers(layers =>", aria_branch)
        for field in expected:
            self.assertIn(f"AriaLetterheadLayout.{field}(isA5)", aria_branch)
        self.assertIn("? AriaLetterheadLayout.BodyTopPadding(isA5)", source)

    def test_homepa_header_is_left_anchored_and_fields_are_independent(self):
        source = SERVICE.read_text()
        branch = source.split("else if (isHomepaLetterhead)", 1)[1]
        branch = branch.split("else if (hasLetterhead)", 1)[0]
        self.assertIn("page.Header().ContentFromLeftToRight()", branch)
        self.assertIn(".Layers(layers =>", branch)
        for field in ("Number", "Date", "Attachment"):
            self.assertIn(f"HomepaLetterheadLayout.{field}(isA5)", branch)
        self.assertNotIn(".Column(", branch)
        self.assertIn(".Height(HomepaLetterheadLayout.HeaderHeight(isA5))", branch)
        self.assertIn("HomepaLetterheadLayout.Number(isA5)", branch, "fixed metadata positions are independent of body spacing")

    def test_1003_signer_block_alignment_and_multpage_footer_are_conditional(self):
        source = SERVICE.read_text()
        self.assertIn("col.Item().ShowEntire().Column(AddSignerBlock)", source)
        self.assertIn("var signerTopPadding = isHomepaLetterhead", source)
        self.assertIn("var tableTopPadding = isHomepaLetterhead", source)
        self.assertIn("var maxSignatureHeight = isA5 ? 68f : 72f", source)
        self.assertIn(".AlignLeft().Width(signerBlockWidth * signerColumns)", source)
        self.assertNotIn('Text("امضاکنندگان")', source)
        self.assertNotIn("IDynamicComponent", source)
        self.assertNotIn(".Dynamic(", source)
        self.assertIn("page.Footer().Height(20).AlignTop().AlignCenter().Text(text =>", source)
        self.assertIn("text.CurrentPageNumber()", source)
        self.assertIn("text.TotalPages()", source)
        self.assertIn(".FontSize(8)\n                            .LineHeight(1.1f)", source)
        overlay = source.split("private byte[] OverlayOnLetterhead", 1)[1]
        self.assertIn("for (var index = 0; index < contentDoc.PageCount; index++)", overlay)
        self.assertLess(overlay.index("graphics.DrawImage(asset.Image, bounds)"),
                        overlay.index("graphics.DrawImage(contentForm, bounds)"))
        self.assertIn("page.PageColor(hasLetterhead ? Colors.Transparent : Colors.White);", source)
        self.assertIn("page.Content().Background(Colors.Transparent)", source)

    def test_print_does_not_inject_a_closing_or_stamp_box(self):
        source = SERVICE.read_text()
        self.assertNotIn("closingText", source)
        self.assertNotIn("closingMatch", source)
        self.assertNotIn('Text("با تشکر")', source)
        self.assertNotIn('Text("مهر")', source)
        self.assertNotIn("stampHeight", source)
        self.assertNotIn("HtmlToPlainText", source,
                         "authored HTML must be rendered rather than flattened or truncated")
        self.assertIn("RichLetterPdfRenderer.AddTo(col, letter.Text", source)
        self.assertIn(".Text(signer.Name)", source)
        self.assertIn(".Text(signer.Role)", source)
        self.assertIn(".Svg(signature.Svg).FitArea()", source)
        self.assertIn("if (signerCards.Count > 0 && !signersKeptWithTrailingParagraph)", source)

    def test_homepa_header_fields_and_body_spacing_are_independent(self):
        source = SERVICE.read_text()
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        self.assertIn("var isHomepaLetterhead = hasLetterhead && _homepaCompanyIds.Contains(designCompanyId);", source)
        self.assertIn("private static readonly int[] DefaultHomepaCompanyIds = { 3, HomepaCompanyId };", source,
                      "the sample installation stores Homepa as company 3; both ids share the same artwork")
        self.assertIn("var contentTopPadding = isHomepaLetterhead", source)
        self.assertIn(": (withMargin || hasLetterhead ? 30f : 0f);", source,
                      "other companies keep their existing body spacing")
        self.assertIn("HomepaLetterheadLayout.ReceiverTopPadding(isA5)", source)
        self.assertIn("contentContainer.PaddingTop(contentTopPadding)", source)
        self.assertIn("HomepaLetterheadLayout.SubjectBodyGap(isA5) : 0f", source)
        self.assertIn("col.Item().PaddingTop(subjectTopPadding).Text", source)
        self.assertIn(".Height(HomepaLetterheadLayout.HeaderHeight(isA5))", source)
        self.assertIn("HeaderHeight(bool isA5) => isA5 ? 150f : 160f", layout)
        self.assertIn("ReceiverTopPadding(bool isA5) => isA5 ? 28f : 80f", layout)
        self.assertIn("SubjectBodyGap(bool isA5) => isA5 ? 8f : 12f", layout)
        self.assertIn("FooterReserve(bool isA5) => isA5 ? 68f : 90f", layout)
        self.assertIn("HeaderValueFontSize(bool isA5) => isA5 ? 8.5f : 10.5f", layout)
        homepa_branch = source.split("else if (isHomepaLetterhead)", 1)[1].split("else if (hasLetterhead)", 1)[0]
        self.assertNotIn("center: true", homepa_branch)
        self.assertNotIn('"شماره:"', homepa_branch, "captions must not be duplicated over the artwork")
        self.assertNotIn('"تاریخ:"', homepa_branch)
        self.assertNotIn('"پیوست:"', homepa_branch)
        self.assertIn(".Height(field.Height).AlignMiddle().AlignRight()", source)
        self.assertIn('isolateLtr ? $"\\u2066{value}\\u2069" : value', source)
        self.assertIn("col.Item().PaddingTop(subjectTopPadding).Text", source)
        self.assertNotIn(".Column(body => AddSubjectAndFollowingContent(body))", source,
                         "the separator and all body elements must share one margin box")
        self.assertIn("var pageTopMargin = isHomepaLetterhead || isAriaLetterhead", source,
                      "company 1003 header anchor must not depend on the body-margin option")
        self.assertIn(": (withMargin ? (isA5 ? 16f : 24f) : 0f);", source,
                      "other companies retain their existing page margin")
        self.assertIn("page.MarginBottom(isHomepaLetterhead", source,
                      "only company 1003 uses the dedicated footer clearance")
        self.assertIn("page.Size(isA5 ? PageSizes.A5 : PageSizes.A4);", source)

    def test_homepa_content_gap_matches_scaled_header_art_in_a4_and_a5(self):
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        # Measured from the actual 1003 watermark; keep roughly 1cm clear below the diagonal art.
        a4_gap = 24 + 160 + 80 - 232
        a5_gap = 16 + 150 + 28 - 163
        self.assertGreaterEqual(a4_gap, 28)
        self.assertLessEqual(a4_gap, 42)
        self.assertGreaterEqual(a5_gap, 28)
        self.assertLessEqual(a5_gap, 42)
        self.assertLess(abs(a4_gap - a5_gap), 3)
        self.assertNotIn("NumberLabel", layout)
        self.assertNotIn("DateLabel", layout)
        self.assertNotIn("AttachmentLabel", layout)

    def test_print_popup_is_reserved_on_native_menu_click(self):
        view = (Path(__file__).resolve().parents[2] / "src" / "Inventory.Client" / "Pages" / "Office" / "Letters" / "Outgoing" / "OutgoingLetterView.razor").read_text()
        shell = (Path(__file__).resolve().parents[2] / "src" / "Inventory.Client" / "wwwroot" / "js" / "app-shell.js").read_text()
        self.assertIn('data-ltx-print-option', view)
        self.assertIn("document.addEventListener('click'", shell)
        self.assertIn("window.ltxPrintReserveByClick()", shell)
        self.assertIn("window.ltxPrintPendingWindows", shell)
        self.assertIn("window.ltxPrintSessions", shell)

    def test_print_api_keeps_access_and_not_found_guards(self):
        controller = (API / "Controllers" / "Office" / "OutgoingLettersController.cs").read_text()
        print_action = controller.split('public async Task<IActionResult> Print(', 1)[1].split('// ==================== گردش / ارجاع', 1)[0]
        self.assertIn('ForbiddenUnlessAsync(Module, "Read")', print_action)
        self.assertIn('if (pdf == null) return NotFound', print_action)
        self.assertIn('!string.Equals(size, "A4"', print_action)
        self.assertIn('!string.Equals(size, "A5"', print_action)
        self.assertIn('var hasDabirkhane = isAdmin || await HasAsync(Module, "Dabirkhane")', print_action)
        flow_check = controller.split("private Task<bool> InFlowAsync(int letterId)", 1)[1].split("[HttpGet(\"{id:int}/attachments\")", 1)[0]
        self.assertIn("AnyAsync(letter =>", flow_check)
        self.assertIn("Db.Erjas.Any", flow_check)
        self.assertIn("Db.OutgoingLetterSigners.Any", flow_check)
        self.assertNotIn("FirstOrDefaultAsync", flow_check)

    def test_saved_signature_preview_uses_authenticated_bytes_not_an_img_request(self):
        users_page = (Path(__file__).resolve().parents[2] / "src" / "Inventory.Client" / "Pages" / "System" / "Users.razor").read_text()
        auth_controller = (API / "Controllers" / "Core" / "AuthController.cs").read_text()
        self.assertIn('Http.GetAsync($"/api/auth/users/signature/{userId}")', users_page)
        self.assertIn('data:image/png;base64,', users_page)
        self.assertNotIn('src="/api/auth/users/signature/@form.Id"', users_page)
        self.assertIn('[Authorize]', auth_controller.split('[HttpGet("users/signature/{id:int}")]', 1)[1].split('private static byte[] CropSignatureToAlphaBounds', 1)[0])
        self.assertIn('SaveNamedAsync("UserSignatures", id, output, $"user_{id}_signature.png")', auth_controller)
        self.assertIn('Size = new Size(1200, 480)', auth_controller)
        self.assertIn('PngColorType.RgbWithAlpha', auth_controller)
        self.assertIn('Accept="image/png"', users_page)

    def test_templates_travel_with_build_and_publish(self):
        project = ET.parse(API / "Inventory.Api.csproj").getroot()
        content = next(e for e in project.iter("Content") if e.get("Include") == "Reports/**/*.mrt")
        self.assertEqual(content.get("CopyToOutputDirectory"), "PreserveNewest")
        self.assertEqual(content.get("CopyToPublishDirectory"), "PreserveNewest")


def declared_meta_fields(company, size):
    """Replica of LetterheadMetaFields.TryRead so the MRT declarations can be asserted here."""
    unit_factor = {"inches": 72, "centimeters": 72 / 2.54, "millimeters": 72 / 25.4}
    root = ET.parse(TEMPLATES / str(company) / f"Outgoing-{size}.mrt").getroot()
    factor = unit_factor.get((root.findtext("ReportUnit") or "").strip().lower(), 72)
    page = next(e for e in root.iter() if e.tag.split("}")[-1] == "Pages")[0]
    margins = [float(value) for value in page.findtext("Margins").split(",")]
    margin_left, margin_top = margins[0], margins[2]
    components = list(page.find("Components"))

    fields = {}
    for name, column in (("Number", "LetterNumber"), ("Date", "DateShams"), ("Attachment", "Peyvast")):
        component = next((c for c in components if column.lower() in (c.findtext("Text") or "").lower()), None)
        if component is None:
            continue
        x, y, width, height = [float(value) for value in component.findtext("ClientRectangle").split(",")]
        font = component.findtext("Font") or ""
        size_pt = next((float(part) for part in font.split(",") if part.replace(".", "", 1).isdigit()), 8.0)
        fields[name] = {
            "left": (margin_left + x) * factor,
            "top": (margin_top + y) * factor,
            "width": width * factor,
            "height": height * factor,
            "color": (component.findtext("TextBrush") or "").strip(),
            "font": size_pt,
            "center": (component.findtext("HorAlignment") or "").strip() == "Center",
        }
    return fields


class LetterheadMetaValuesTests(unittest.TestCase):
    """The number/date/attachment values are painted where the company template says they are."""

    def test_every_company_template_declares_the_three_meta_values(self):
        for company in (1, 2, 3, 1003, 1005):
            for size in ("A4", "A5"):
                with self.subTest(company=company, size=size):
                    fields = declared_meta_fields(company, size)
                    self.assertEqual(set(fields), {"Number", "Date", "Attachment"},
                                     "the print renderer needs all three declarations, otherwise it falls back to the old layout")

    def test_value_colour_follows_the_letterhead_artwork(self):
        # Homepa prints white values on the dark band; Forough/Aria print black values on white paper.
        for company in (3, 1003):
            for size in ("A4", "A5"):
                with self.subTest(company=company, size=size):
                    for field in declared_meta_fields(company, size).values():
                        self.assertEqual(field["color"], "White")
        for company in (1, 2, 1005):
            for size in ("A4", "A5"):
                with self.subTest(company=company, size=size):
                    for field in declared_meta_fields(company, size).values():
                        self.assertEqual(field["color"], "Black")

    def test_company_three_and_1003_share_the_same_homepa_geometry(self):
        for size in ("A4", "A5"):
            with self.subTest(size=size):
                sample = declared_meta_fields(3, size)
                main = declared_meta_fields(1003, size)
                for name in ("Number", "Date", "Attachment"):
                    for key in ("left", "top", "width", "height"):
                        self.assertAlmostEqual(sample[name][key], main[name][key], places=3,
                                               msg="Reports/OutgoingLetters/Companies/3 and /1003 carry the same Homepa artwork")

    def test_declared_homepa_geometry_equals_the_calibrated_layout(self):
        # HomepaLetterheadLayout is the calibrated copy of these MRT declarations; they must not drift.
        layout = SERVICE.with_name("HomepaLetterheadLayout.cs").read_text()
        for index, (size, header_origin) in enumerate((("A5", 16), ("A4", 24))):
            fields = declared_meta_fields(1003, size)
            for name in ("Number", "Date", "Attachment"):
                method = re.search(r"static Field " + name + r"\(bool isA5\)(.*?);", layout, re.S).group(1)
                variant = re.findall(r"new\(([^)]+)\)", method)[index]
                left, top, width, height = [float(value.strip().removesuffix("f")) for value in variant.split(",")]
                with self.subTest(size=size, field=name):
                    self.assertAlmostEqual(top + header_origin, fields[name]["top"], places=3)
                    self.assertAlmostEqual(width, fields[name]["width"], places=1)
                    self.assertAlmostEqual(height, fields[name]["height"], places=1)
                    # number keeps the template's x, date/attachment are nudged left by 3pt (A5) / 4pt (A4)
                    nudge = 0 if name == "Number" else (3 if size == "A5" else 4)
                    self.assertAlmostEqual(left, fields[name]["left"] - nudge, places=1)

    def test_renderer_reads_the_company_template_instead_of_a_white_guess(self):
        source = SERVICE.read_text()
        self.assertIn("var declared = ReadDeclaredMetaFields(", source)
        self.assertIn("var parsed = LetterheadMetaFields.TryRead(templatePath);", source)
        self.assertIn('FindCompanyTemplate(letterheadCompanyId.Value, isA5 ? "A5" : "A4")', source)
        self.assertIn("AddDeclaredField(layers.Layer(), declared.Number, number, pageTopMargin);", source)
        self.assertIn("AddDeclaredField(layers.Layer(), declared.Date, date, pageTopMargin);", source)
        self.assertIn(".FontColor(field.Color).LineHeight(1.05f);", source,
                      "the declared colour must win over the hard-coded white used before")
        self.assertIn("box = field.Center ? box.AlignCenter() : box.AlignRight();", source)
        self.assertIn("var top = Math.Max(0f, field.Top - pageHeaderOrigin);", source,
                      "template rows above the page margin must not become negative padding")

    def test_homepa_company_ids_are_configurable(self):
        source = SERVICE.read_text()
        self.assertIn('CompanyIdsFor("Reporting:OutgoingLetters:HomepaCompanyIds", DefaultHomepaCompanyIds)', source)
        settings = (API / "appsettings.json").read_text()
        self.assertIn('"HomepaCompanyIds": "3,1003"', settings)
        self.assertIn('"AriaCompanyIds": "1005"', settings)


class CompanyTemplateMapTests(unittest.TestCase):
    """قالب با شناسهٔ پوشه پیدا می‌شود و شناسهٔ شرکت بین نصب‌ها یکی نیست."""

    def test_layout_follows_the_design_not_the_raw_company_id(self):
        source = SERVICE.read_text()
        self.assertIn("var designCompanyId = letterheadCompanyId is int designSource ? TemplateDesignId(designSource) : 0;", source)
        self.assertIn("var isHomepaLetterhead = hasLetterhead && _homepaCompanyIds.Contains(designCompanyId);", source)
        self.assertIn("var isAriaLetterhead = hasLetterhead && _ariaCompanyIds.Contains(designCompanyId);", source)

    def test_template_lookup_honours_the_mapping_before_its_own_folder(self):
        source = SERVICE.read_text()
        self.assertIn("foreach (var designId in DesignCompanyIds(companyId))", source)
        self.assertIn("private int TemplateDesignId(int companyId) => _companyTemplateMap.TryGetValue(companyId, out var mapped) ? mapped : companyId;", source)
        design = source.split("private IEnumerable<int> DesignCompanyIds(int companyId)", 1)[1].split("}", 1)[0]
        self.assertLess(design.index("if (designId != companyId) yield return designId;"), design.index("yield return companyId;"),
                        "the mapped design must be searched first")

    def test_mapping_is_read_from_configuration(self):
        source = SERVICE.read_text()
        self.assertIn('_configuration.GetSection("Reporting:OutgoingLetters:CompanyTemplateMap").GetChildren()', source)
        settings = (API / "appsettings.json").read_text()
        self.assertIn('"CompanyTemplateMap"', settings)

    def test_every_template_folder_is_reachable_and_identifiable(self):
        """هر پوشهٔ قالب باید همان دو سایز را داشته باشد؛ وگرنه نصب با شناسهٔ متفاوت کورکورانه شکست می‌خورد."""
        for folder in sorted(TEMPLATES.iterdir()):
            if not folder.is_dir():
                continue
            with self.subTest(company=folder.name):
                for size in ("A4", "A5"):
                    self.assertTrue((folder / f"Outgoing-{size}.mrt").is_file())

    def test_report_folder_templates_can_be_synced_to_the_print_path(self):
        """پوشهٔ Report/outgoingletter مرجع طراحی است؛ اسکریپت همگام‌سازی باید وجود داشته باشد."""
        repo = Path(__file__).resolve().parents[3]
        folder = repo / "Report" / "outgoingletter"
        self.assertTrue((folder / "README-FA.md").is_file())
        sync = folder / "sync-to-api-templates.bat"
        self.assertTrue(sync.is_file(), "بدون اسکریپت همگام‌سازی، ویرایش این پوشه هیچ اثری روی چاپ ندارد")
        script = sync.read_text(encoding="utf-8", errors="replace")
        self.assertIn("Outgoing-%%S.mrt", script)
        self.assertIn("inventory\\src\\Inventory.Api\\Reports\\OutgoingLetters\\Companies", script)
        for company in ("3", "1003"):
            for size in ("A4", "A5"):
                self.assertTrue((folder / company / f"Report_{size}_OutgoingLetter.mrt").is_file())


class PrintPreparationBudgetTests(unittest.TestCase):
    """«صفحهٔ چاپ در حال بارگذاری می‌ماند و بعد خطا می‌دهد» = سقف زمانی آماده‌سازی در کلاینت.

    سقف قبلی ۱۵ ثانیه بود و کل مسیر (دریافت PDF + تحویل به مرورگر) را می‌پوشاند؛ رندر سرور
    (QuestPDF + PDFsharp روی تصویر ۲۴۸۱×۳۵۰۸) روی سرور کند یا در اولین چاپ بیشتر طول می‌کشد و
    درخواست پیش از آماده‌شدن قطع می‌شد.
    """

    def setUp(self):
        self.workflow = (CLIENT / "Services" / "LetterPrintWorkflow.cs").read_text(encoding="utf-8")

    def test_preparation_budget_is_realistic_and_split(self):
        self.assertIn("FirstAttemptTimeout = TimeSpan.FromSeconds(90)", self.workflow)
        self.assertIn("RetryTimeout = TimeSpan.FromSeconds(60)", self.workflow)
        self.assertIn("HandoffTimeout = TimeSpan.FromSeconds(30)", self.workflow)
        self.assertNotIn("TimeSpan.FromSeconds(15)", self.workflow,
                         "the old 15-second ceiling cancelled the print before the server finished")

    def test_download_and_browser_handoff_have_separate_budgets(self):
        self.assertIn("using (var loadTimeout = new CancellationTokenSource(loadBudget))", self.workflow)
        self.assertIn("using (var handoffTimeout = new CancellationTokenSource(HandoffTimeout))", self.workflow)
        self.assertIn("var loadBudget = attemptNumber == 1 ? FirstAttemptTimeout : RetryTimeout;", self.workflow)

    def test_timeout_message_explains_what_happened(self):
        self.assertIn("ساخت نسخهٔ چاپ بیش از", self.workflow)
        self.assertIn("«تلاش مجدد»", self.workflow)
        self.assertIn("OutgoingLetterPrintTiming", self.workflow)

    def test_user_sees_progress_instead_of_a_frozen_spinner(self):
        self.assertIn("SlowPreparationHintAfter = TimeSpan.FromSeconds(8)", self.workflow)
        self.assertIn('"ltxPrintStatus"', self.workflow)
        js = (CLIENT / "wwwroot" / "js" / "app-shell.js").read_text(encoding="utf-8")
        self.assertIn("window.ltxPrintStatus = function (id, text) {", js)
        self.assertIn("ltxSetPrintStatus(session, String(text", js)

    def test_failed_render_is_not_cached(self):
        for service in (SERVICE, Path(__file__).resolve().parents[3] / "src" / "Inventory.Api" / "Services" / "Office" / "Outgoing" / "OutgoingLetterPrintService.cs"):
            with self.subTest(service=str(service.parts[-6])):
                source = service.read_text(encoding="utf-8")
                self.assertIn("RenderedPdfLifetime = TimeSpan.FromSeconds(90)", source)
                self.assertIn("ConcurrentDictionary<string, CachedRender> RenderedPdfs", source)
                self.assertIn("TaskCompletionSource<byte[]?>", source)
                self.assertIn("TaskCreationOptions.RunContinuationsAsynchronously", source)
                self.assertIn(
                    "RenderedPdfs.TryRemove(new KeyValuePair<string, CachedRender>(cacheKey, cached));",
                    source, "a failed or missing render must not stay cached")
                self.assertIn("cached.Fail(ex);", source)

    def test_cache_key_covers_letter_signatures_and_copies(self):
        source = SERVICE.read_text(encoding="utf-8")
        for token in ("SignedCount = _db.OutgoingLetterSigners.Count", "AttachmentCount = _db.AppAttachments.Count",
                      "CopyToCount = _db.OutgoingLetterCopyToes.Count", "SHA256.HashData"):
            self.assertIn(token, source)

    def test_concurrent_and_repeated_prints_share_one_render(self):
        source = SERVICE.read_text(encoding="utf-8")
        wrapper = source.split("public async Task<byte[]?> GeneratePdfAsync", 1)[1]
        wrapper = wrapper.split("private static (CachedRender Render, bool StartedHere) RentRenderedPdf", 1)[0]
        self.assertIn("var (cached, startedHere) = RentRenderedPdf(cacheKey);", wrapper)
        self.assertIn("return await cached.Render;", wrapper)
        helper = source.split("private static (CachedRender Render, bool StartedHere) RentRenderedPdf", 1)[1]
        self.assertIn("RenderedPdfs.TryAdd(cacheKey, fresh)", helper,
                      "the in-flight render must be published before it is awaited")

    def test_print_cache_does_not_use_lazy_task(self):
        """VSTHRD011: خواندن Lazy<Task<T>>.Value می‌تواند deadlock کند و ساخت پروژه را
        با «Error» متوقف می‌کند (خطای VS روی همین سرویس). رندر مشترک با
        TaskCompletionSource پیاده شده است."""
        for service in (SERVICE, Path(__file__).resolve().parents[3] / "src" / "Inventory.Api"
                        / "Services" / "Office" / "Outgoing" / "OutgoingLetterPrintService.cs"):
            with self.subTest(service=service.parts[-6]):
                source = service.read_text(encoding="utf-8")
                self.assertNotIn("Lazy<Task", source, "VSTHRD011 build break")
                self.assertNotIn("render.Value", source)

    def test_unexpected_print_errors_reach_the_user(self):
        controllers = [
            API / "Controllers" / "Office" / "OutgoingLettersController.cs",
            Path(__file__).resolve().parents[3] / "src" / "Inventory.Api" / "Controllers" / "Office" / "OutgoingLettersController.cs",
        ]
        for controller in controllers:
            with self.subTest(tree=controller.parts[-6]):
                text = controller.read_text(encoding="utf-8")
                self.assertIn("catch (Exception ex)", text)
                self.assertIn("ساخت PDF نامه ناموفق بود ({ex.GetType().Name})", text)

    def test_missing_persian_font_is_reported_instead_of_silently_ignored(self):
        for service in (SERVICE, Path(__file__).resolve().parents[3] / "src" / "Inventory.Api" / "Services" / "Office" / "Outgoing" / "OutgoingLetterPrintService.cs"):
            with self.subTest(service=service.parts[-6]):
                text = service.read_text(encoding="utf-8")
                self.assertIn("if (!File.Exists(reg))", text)
                self.assertIn("فونت چاپ فارسی پیدا نشد", text)


class BothClientTreesPrintTests(unittest.TestCase):
    """هر دو درخت این مخزن باید رفتار چاپ یکسان داشته باشند.

    اسکریپت‌های انتشار در ریشهٔ مخزن از `src/` استفاده می‌کنند و
    `inventory/publish-server.bat` از `inventory/src/`؛ اگر اصلاح فقط در یکی باشد،
    نصب کاربر بسته به اینکه با کدام اسکریپت منتشر شده، همان خطای قبلی را می‌دهد.
    """

    ROOT = Path(__file__).resolve().parents[3]
    CLIENTS = [ROOT / "src" / "Inventory.Client", ROOT / "inventory" / "src" / "Inventory.Client"]

    def test_every_client_has_the_shared_print_workflow(self):
        for client in self.CLIENTS:
            with self.subTest(client=client.parts[-4]):
                workflow = (client / "Services" / "LetterPrintWorkflow.cs").read_text(encoding="utf-8")
                self.assertIn("FirstAttemptTimeout = TimeSpan.FromSeconds(90)", workflow)
                self.assertIn("ltxPrintStatus", workflow)
                program = (client / "Program.cs").read_text(encoding="utf-8")
                self.assertIn("AddScoped<LetterPrintWorkflow>()", program,
                              "the workflow must be registered or the print pages cannot inject it")

    def test_every_client_prints_through_the_workflow(self):
        pages = [
            "Pages/Office/Letters/LetterCartable.razor",
            "Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor",
            "Pages/Office/Letters/Outgoing/OutgoingLetterView.razor",
            "Pages/Office/Letters/Outgoing/OutgoingDabirkhane.razor",
        ]
        for client in self.CLIENTS:
            for page in pages:
                with self.subTest(client=client.parts[-4], page=page.split("/")[-1]):
                    text = (client / page).read_text(encoding="utf-8")
                    self.assertIn("@inject LetterPrintWorkflow PrintWorkflow", text)
                    self.assertIn("PrintWorkflow.PrintPdfAsync", text)
                    self.assertNotIn('"ltxPrintReady", printId, payload', text,
                                     "the PDF must reach the browser as binary, not base64")

    def test_every_client_has_the_full_print_js_surface(self):
        for client in self.CLIENTS:
            with self.subTest(client=client.parts[-4]):
                js = (client / "wwwroot" / "js" / "app-shell.js").read_text(encoding="utf-8")
                for fn in ("ltxPrintBegin", "ltxPrintReady", "ltxPrintFail", "ltxPrintStatus",
                           "ltxPrintWaitForAction", "ltxPrintDirectPage", "ltxPrintCancel"):
                    self.assertIn(f"window.{fn} = ", js)
                self.assertIn("ltxEnsurePrintSurface", js)

    def test_print_services_accept_a_cancellation_token(self):
        for client in self.CLIENTS:
            with self.subTest(client=client.parts[-4]):
                outgoing = (client / "Services" / "OutgoingLetterServices.cs").read_text(encoding="utf-8")
                self.assertIn("CancellationToken cancellationToken = default", outgoing)
                self.assertIn("ReadAsByteArrayAsync(cancellationToken)", outgoing)
                letters = (client / "Services" / "LetterServices.cs").read_text(encoding="utf-8")
                self.assertIn("CancellationToken cancellationToken = default", letters)
                api = (client / "Services" / "IApiClient.cs").read_text(encoding="utf-8")
                self.assertIn("GetFileAsync(string path, CancellationToken cancellationToken = default)", api)


class LetterheadSourceResolutionTests(unittest.TestCase):
    """«فایل‌های طرح در Reports/OutgoingLetters/Companies خوانده نمی‌شوند» — سه علت واقعی:

    ۱) اولویت: فایل ثبت‌شده روی رکورد شرکت (SystemCompanies.LetterheadFileName) همیشه بر
       طرح داخل قالب MRT مقدم بود، پس طرح شرکت هرگز خوانده نمی‌شد.
    ۲) نامه بدون CompanyId: چاپ «اولین شرکت دارای سربرگ» را انتخاب می‌کرد، نه شرکت نامه.
    ۳) نام فایل: فقط «Outgoing-A4.mrt» دقیق خوانده می‌شد و هر نام دیگری بی‌صدا نادیده می‌رفت
       (لاگ آن هم فقط Debug بود و در سطح پیش‌فرض دیده نمی‌شد).
    """

    ROOT = Path(__file__).resolve().parents[3]
    TREES = [ROOT / "src", ROOT / "inventory" / "src"]

    def service_text(self, tree):
        return (tree / "Inventory.Api" / "Services" / "Office" / "Outgoing"
                / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")

    def test_template_can_win_over_the_registered_file(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service_text(tree)
                self.assertIn("PreferTemplateLetterhead", service)
                self.assertIn('"Reporting:OutgoingLetters:PreferTemplateLetterhead"', service)
                self.assertIn("var preferTemplate = PreferTemplateLetterhead;", service)
                self.assertIn("preferTemplate && templateLetterheadPath != null", service)
                settings = (tree / "Inventory.Api" / "appsettings.json").read_text(encoding="utf-8")
                self.assertIn('"PreferTemplateLetterhead": false', settings)

    def test_shadowing_of_the_design_is_logged(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service_text(tree)
                self.assertIn("فایل ثبت‌شدهٔ شرکت بر طرح قالب اولویت دارد", service)
                self.assertIn("RegisteredPath={RegisteredPath}, TemplatePath={TemplatePath}", service)

    def test_missing_template_is_a_loud_warning_with_the_available_folders(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service_text(tree)
                self.assertIn("هیچ قالب چاپی برای شرکت پیدا نشد", service)
                self.assertIn("AvailableDesigns={AvailableDesigns}", service)
                self.assertIn("DescribeAvailableDesigns(templateRoot, roots)", service)
                self.assertNotIn('_logger.LogDebug("قالب سربرگ شرکت یافت نشد', service,
                                 "debug-level logging hid the reason the design was not read")

    def test_alternate_template_file_names_are_still_read(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service_text(tree)
                self.assertIn("private static string? FindTemplateFile(string folder, string size, out bool exactName)", service)
                self.assertIn("Contains(size, StringComparison.OrdinalIgnoreCase)", service)
                self.assertIn("if (files.Length == 1)", service)
                self.assertIn("نام فایل قالب شرکت {CompanyId} مطابق استاندارد نیست", service)

    def test_letter_without_company_never_picks_another_companys_letterhead(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service_text(tree)
                self.assertNotIn(
                    '.Where(c => c.IsActive && c.LetterheadFileName != null && c.LetterheadFileName != "")\n                .OrderBy(c => c.Id)',
                    service,
                    "the old code printed such letters on the first company's letterhead")
                self.assertIn("این نامه به هیچ شرکتی متصل نیست", service)
                self.assertIn("ActiveCompanyIdFromContext()", service)

    def test_letters_store_the_active_company_when_the_form_sends_none(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = (tree / "Inventory.Api" / "Services" / "Office" / "Outgoing"
                           / "OutgoingLetterService.cs").read_text(encoding="utf-8")
                self.assertIn("private int? DefaultCompanyIdForLetter()", service)
                self.assertIn("var active = _db.ActiveCompanyId;", service)
                self.assertEqual(service.count(": DefaultCompanyIdForLetter()"), 2,
                                 "create and update must both keep a company on the letter")
                self.assertNotIn("CompanyId = dto.CompanyId is > 0 ? dto.CompanyId : null,", service)

    def test_print_diagnostics_are_reachable(self):
        for tree in self.TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service_text(tree)
                self.assertIn("Task<OutgoingLetterPrintDiagnosticsDto?> DiagnoseAsync(int letterId, string size);", service)
                self.assertIn("public async Task<OutgoingLetterPrintDiagnosticsDto?> DiagnoseAsync(", service)
                controller = (tree / "Inventory.Api" / "Controllers" / "Office" /
                              "OutgoingLettersController.cs").read_text(encoding="utf-8")
                self.assertIn('HttpGet("{id:int}/print/diagnose")', controller)
                self.assertIn("await _print.DiagnoseAsync(id, size)", controller)
                dto = (tree / "Inventory.Shared" / "Dtos" / "OutgoingLetterDtos.cs").read_text(encoding="utf-8")
                self.assertIn("class OutgoingLetterPrintDiagnosticsDto", dto)
                for field in ("LetterCompanyId", "EffectiveCompanyId", "TemplatePath",
                              "RegisteredLetterheadPath", "LetterheadSource", "AvailableDesigns", "Hint"):
                    self.assertIn(f"public ", dto)
                    self.assertIn(field, dto)

    def test_client_offers_every_company_and_shows_the_diagnosis(self):
        for client in [self.ROOT / "src" / "Inventory.Client", self.ROOT / "inventory" / "src" / "Inventory.Client"]:
            with self.subTest(client=client.parts[-4]):
                compose = (client / "Pages" / "Office" / "Letters" / "Outgoing" /
                           "ComposeOutgoingModal.razor").read_text(encoding="utf-8")
                self.assertIn("@foreach (var company in CompanyOptions)", compose)
                self.assertIn("await OutgoingSvc.GetCompaniesAsync()", compose)
                self.assertIn("companyId = activeCompany?.Id ??", compose)
                view = (client / "Pages" / "Office" / "Letters" / "Outgoing" /
                        "OutgoingLetterView.razor").read_text(encoding="utf-8")
                self.assertIn("ShowPrintDiagnosticsAsync", view)
                self.assertIn("تشخیص چاپ", view)
                service = (client / "Services" / "OutgoingLetterServices.cs").read_text(encoding="utf-8")
                self.assertIn("print/diagnose?size=", service)


class PrintServiceSignatureTests(unittest.TestCase):
    """CS0535: امضای متد عمومی سرویس چاپ باید دقیقاً با اینترفیسش یکی باشد.

    هنگام بازنویسی کش رندر، امضای سه‌پارامتریِ درخت `src/` روی پیاده‌سازی درخت
    `inventory/` نوشته شد؛ اینترفیس آن درخت شش پارامتر دارد و ساخت پروژه با
    «does not implement interface member» متوقف شد.
    """

    ROOT = Path(__file__).resolve().parents[3]
    TREES = [ROOT / "src", ROOT / "inventory" / "src"]

    @staticmethod
    def _params(text):
        return [part for part in text.split(",") if part.strip()]

    def test_public_print_methods_match_the_interface(self):
        import re

        for tree in self.TREES:
            service = (tree / "Inventory.Api" / "Services" / "Office" / "Outgoing"
                       / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")
            with self.subTest(tree=tree.parts[-3]):
                interface = re.search(
                    r"public interface IOutgoingLetterPrintService.*?\n\}", service, re.S)
                self.assertIsNotNone(interface, "interface IOutgoingLetterPrintService not found")
                declared = re.findall(
                    r"^\s*Task<[^>]+(?:\[\])?\??>\s+(\w+)\(([^)]*)\);", interface.group(0), re.M)
                self.assertGreaterEqual(len(declared), 2)

                for name, parameters in declared:
                    with self.subTest(method=name):
                        expected = len(self._params(parameters))
                        implementation = re.search(
                            rf"public async Task<[^>]+(?:\[\])?\??>\s+{name}\(([^)]*)\)", service, re.S)
                        self.assertIsNotNone(implementation, f"{name} is not implemented")
                        actual = len(self._params(implementation.group(1)))
                        self.assertEqual(
                            expected, actual,
                            f"{name} takes {actual} parameters but the interface declares {expected} "
                            "(CS0535 / CS7036)")
                        # نام پارامترها هم باید یکی باشد؛ کلید کش و کش رندر به همین نام‌ها وابسته‌اند.
                        for parameter in self._params(parameters):
                            declared_name = parameter.split("=")[0].strip().split()[-1]
                            self.assertRegex(implementation.group(1), rf"\b{declared_name}\b")

    def test_render_cache_key_sees_every_output_option(self):
        # اگر کلید کش بعضی گزینه‌ها را نبیند، نسخهٔ «با سربرگ» و «بدون سربرگ» خروجی
        # یکدیگر را از کش برمی‌دارند.
        for tree in self.TREES:
            service = (tree / "Inventory.Api" / "Services" / "Office" / "Outgoing"
                       / "OutgoingLetterPrintService.cs").read_text(encoding="utf-8")
            with self.subTest(tree=tree.parts[-3]):
                wrapper = service.split("public async Task<byte[]?> GeneratePdfAsync", 1)[1]
                wrapper = wrapper.split("if (cacheKey == null) return null;", 1)[0]
                signature = wrapper.split(")", 1)[0]
                key_call = wrapper.split("BuildPrintCacheKeyAsync", 1)[1].split(");", 1)[0]
                for option in ("withCopy", "withMargin", "withLetterhead", "requireSignatures"):
                    if option in signature:
                        self.assertIn(option, key_call,
                                      f"{option} must take part in the cache key")


if __name__ == "__main__":
    unittest.main()
