"""Source-contract checks for shared letter printing and rich-text preservation."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
API = ROOT / "src" / "Inventory.Api"
CLIENT = ROOT / "src" / "Inventory.Client"


class RichLetterPrintTests(unittest.TestCase):
    def test_outgoing_pdf_renders_sanitized_html_structure_not_plaintext(self):
        service = (API / "Services/Office/Outgoing/OutgoingLetterPrintService.cs").read_text()
        renderer = (API / "Services/Office/Outgoing/RichLetterPdfRenderer.cs").read_text()
        self.assertNotIn("HtmlToPlainText", service)
        self.assertIn("RichLetterPdfRenderer.AddTo(col, letter.Text", service)
        self.assertIn("RichLetterHtml.Sanitize(html)", renderer)
        for marker in ('case "ol"', 'case "ul"', 'case "table"', 'case "blockquote"'):
            self.assertIn(marker, renderer)
        self.assertIn('node.Tag is "strong" or "b"', renderer)
        self.assertIn('node.Tag is "em" or "i"', renderer)
        self.assertIn('text.Span("\\n")', renderer)
        self.assertIn(".DirectionFromRightToLeft()", renderer)
        self.assertIn("text.AlignRight();", renderer)
        self.assertIn("PersianDigits(index.ToString", renderer)

    def test_questpdf_text_action_callbacks_do_not_chain_void_return_values(self):
        service = (API / "Services/Office/Outgoing/OutgoingLetterPrintService.cs").read_text()
        renderer = (API / "Services/Office/Outgoing/RichLetterPdfRenderer.cs").read_text()
        invalid_chain = re.compile(r"\}\)\s*\.(?:FontSize|LineHeight|AlignRight|DirectionFromRightToLeft)\s*\(")
        self.assertIsNone(invalid_chain.search(service))
        self.assertIsNone(invalid_chain.search(renderer))
        self.assertIn("text.DefaultTextStyle(style => style", renderer)
        self.assertIn("text.Justify();", renderer)

    def test_markup_is_allow_list_sanitized_before_blazor_render(self):
        sanitizer = (ROOT / "src/Inventory.Shared/Security/RichLetterHtml.cs").read_text()
        self.assertIn('"ol", "ul", "li", "strong", "b", "em", "i"', sanitizer)
        self.assertIn('"p", "div", "br"', sanitizer)
        self.assertIn('"blockquote", "table", "thead"', sanitizer)
        self.assertIn("DangerousBlocks.Replace", sanitizer)
        self.assertIn("name.StartsWith(\"on\"", sanitizer)
        for relative in (
            "Pages/Office/Letters/LetterCartable.razor",
            "Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor",
            "Pages/Office/Letters/Outgoing/OutgoingLetterView.razor",
        ):
            page = (CLIENT / relative).read_text()
            self.assertIn("@((MarkupString)RichLetterHtml.Sanitize(", page)

    def test_letter_body_styles_cover_lists_paragraphs_tables_and_print(self):
        css = (CLIENT / "wwwroot/css/letters.css").read_text()
        for selector in (".letter-body ol", ".letter-body ul", ".letter-body li", ".letter-body strong",
                         ".letter-body blockquote", ".letter-body table", "@media print"):
            self.assertIn(selector, css)
        self.assertIn("direction:rtl", css)
        self.assertIn("overflow-wrap:anywhere", css)

    def test_internal_and_outgoing_use_the_same_print_workflow(self):
        workflow = (CLIENT / "Services/LetterPrintWorkflow.cs").read_text()
        internal = (CLIENT / "Pages/Office/Letters/LetterCartable.razor").read_text()
        outgoing = (CLIENT / "Pages/Office/Letters/Outgoing/OutgoingLetterCartable.razor").read_text()
        outgoing_view = (CLIENT / "Pages/Office/Letters/Outgoing/OutgoingLetterView.razor").read_text()
        outgoing_clerk = (CLIENT / "Pages/Office/Letters/Outgoing/OutgoingDabirkhane.razor").read_text()
        js = (CLIENT / "wwwroot/js/app-shell.js").read_text()
        self.assertIn('"ltxPrintBegin"', workflow)
        self.assertIn('"ltxPrintReady"', workflow)
        self.assertIn('"ltxPrintFail"', workflow)
        self.assertIn('"ltxPrintWaitForAction"', workflow)
        self.assertIn('"ltxPrintDirectPage"', workflow)
        # بودجهٔ زمانی: سقف ثابت ۱۵ ثانیه‌ای کل مسیر را قطع می‌کرد و چاپ هرگز آماده نمی‌شد.
        self.assertIn("FirstAttemptTimeout = TimeSpan.FromSeconds(90)", workflow)
        self.assertIn("CancellationTokenSource(loadBudget)", workflow)
        self.assertIn("PrintWorkflow.PrintPdfAsync", internal)
        self.assertIn("PrintWorkflow.PrintPdfAsync", outgoing)
        self.assertIn("PrintWorkflow.PrintPdfAsync", outgoing_view)
        self.assertIn("PrintWorkflow.PrintPdfAsync", outgoing_clerk)
        self.assertIn("data-ltx-print-option", internal)
        self.assertIn("data-ltx-print-option", outgoing_clerk)
        # هیچ تأخیر کورکورانه‌ای پیش از دانلود نیست؛ تنها تأخیر، راهنمای «در حال ساخت» است.
        self.assertEqual(workflow.count("Task.Delay("), 1)
        self.assertIn("Task.Delay(SlowPreparationHintAfter, cancellationToken)", workflow)
        self.assertIn("LetterPrintClientTiming", workflow)
        self.assertIn('MarkStage("pdf-download")', workflow)
        self.assertIn('MarkStage("browser-print-handoff")', workflow)
        self.assertIn('"ltxPrintReady", handoffTimeout.Token, printId, pdfBytes, fileName,', workflow)
        self.assertNotIn("Convert.ToBase64String(pdfBytes)", workflow)
        self.assertIn("doc.fonts.ready", js)
        self.assertIn("doc.images", js)
        self.assertIn("window.addEventListener('afterprint'", js)
        self.assertIn("ltxPrintCleanup(session, true)", js)
        self.assertIn("@page{size:A4;margin:12mm}", js)
        self.assertIn("Vazirmatn,Tahoma,Arial,sans-serif", js)
        self.assertIn("body.ltx-print-direct-page .no-print", js)
        self.assertIn("payload instanceof Uint8Array", js)
        self.assertIn("payload instanceof ArrayBuffer", js)
        program = (CLIENT / "Program.cs").read_text()
        self.assertIn("AddScoped<LetterPrintWorkflow>()", program)

    def test_performance_stages_and_reusable_assets_are_instrumented(self):
        service = (API / "Services/Office/Outgoing/OutgoingLetterPrintService.cs").read_text()
        self.assertIn("Stopwatch.StartNew()", service)
        for stage in (
            "db-letter-company-attachments", "db-default-company", "db-hr-letterhead-logo",
            "db-signers", "db-semat-titles", "db-copy-to",
            "letterhead-resolution-cache", "signature-read-decode-cache", "questpdf-compose-and-render",
            "pdfsharp-letterhead-overlay",
        ):
            self.assertIn(f'MarkStage("{stage}")', service)
        self.assertIn("Stage=font-registration-or-cache", service)
        self.assertIn("Stage=letterhead-image-pdf-cache-open", service)
        self.assertIn("LetterheadAssets.GetOrAdd", service)
        self.assertIn("outgoing-signature-svg:", service)
        self.assertIn("Stage=signature-svg-cache", service)
        self.assertIn("Stage=questpdf-generate-pdf", service)
        controller = (API / "Controllers/Office/OutgoingLettersController.cs").read_text()
        self.assertIn("Stage=controller-access-checks", controller)
        self.assertIn("Stage=controller-pdf-generation", controller)
        self.assertIn("Stage=db-filename", controller)
        self.assertIn("outgoing-letterhead:", service)
        self.assertIn("GetCachedSignatureSvg", service)
        vectorizer = service.split("private static SignatureSvgData? TryReadSignatureSvg", 1)[1].split(
            "private byte[] BuildContentPdf", 1)[0]
        self.assertIn("var activeLevel = 0;", vectorizer)
        self.assertIn("for (var x = 0; x <= row.Length; x++)", vectorizer)
        band_scan = vectorizer.split("Classify every pixel once", 1)[1].split("});", 1)[0]
        self.assertNotIn("for (var level = 1; level <= 8; level++)", band_scan)
        self.assertIn("var copyTos = withCopy", service)
        self.assertIn(": new List<OutgoingLetterCopyTo>()", service)


if __name__ == "__main__":
    unittest.main()
