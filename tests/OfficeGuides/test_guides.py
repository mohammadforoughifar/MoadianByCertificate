"""Dependency-free source contracts; run: python3 -m unittest discover -s inventory/tests/OfficeGuides -v.
These complement, not replace, the Razor build and browser integration checks.
"""
from pathlib import Path
import re
import unittest

CLIENT = Path(__file__).resolve().parents[2] / "src/Inventory.Client"
OFFICE = CLIENT / "Pages/Office"
GUIDES = OFFICE / "Guides"


class OfficeGuideContracts(unittest.TestCase):
    def test_all_guides_share_the_inner_letter_design(self):
        pages = list(GUIDES.glob("*Guide.razor")) + [OFFICE / "Letters/InnerLetterGuide.razor"]
        pages.remove(GUIDES / "OfficeGuide.razor")
        self.assertEqual(6, len(pages))
        for page in pages:
            with self.subTest(page=page.name):
                text = page.read_text()
                self.assertIn("<OfficeGuide", text)
                self.assertIn('OnBack="OnBack"', text)
                self.assertIn('Steps="QuickSteps"', text)
                self.assertIn('Sections="GuideSections"', text)
                steps = text.split("GuideStep[] QuickSteps", 1)[1].split("GuideLink[]", 1)[0]
                self.assertEqual(3, len(re.findall(r'new\("', steps)))

    def test_toc_targets_are_complete_ordered_and_globally_unique(self):
        seen = set()
        for page in [*GUIDES.glob("*Guide.razor"), OFFICE / "Letters/InnerLetterGuide.razor"]:
            if page.name == "OfficeGuide.razor":
                continue
            text = page.read_text()
            ids = re.findall(r'(?:Id|id)="([a-z-]+)"', text)
            toc = re.findall(r'new\("([a-z-]+)",', text.split("GuideLink[] GuideSections", 1)[1])
            with self.subTest(page=page.name):
                self.assertEqual(ids, toc)
                self.assertGreaterEqual(len(ids), 7)
                self.assertEqual(len(ids), len(set(ids)))
                self.assertFalse(set(ids) & seen)
                seen.update(ids)

    def test_each_module_has_the_right_entry_and_back_action(self):
        for page, components in {
            "Letters/Incoming/IncomingLetterCartable.razor": ["IncomingLetterGuide"],
            "Letters/Outgoing/OutgoingLetterCartable.razor": ["OutgoingLetterGuide"],
            "Letters/Outgoing/OutgoingDabirkhane.razor": ["DabirkhaneGuide"],
            "Email.razor": ["PersonalEmailGuide", "OrganizationalEmailGuide"],
        }.items():
            text = (OFFICE / page).read_text()
            with self.subTest(page=page):
                self.assertIn('aria-pressed="@showGuide"', text)
                self.assertIn('showGuide ? "guide-open"', text)
                self.assertIn('class="guide-workspace"', text)
                self.assertIn('private void BackFromGuide() => showGuide = false;', text)
                for component in components:
                    self.assertIn(f'<{component} OnBack="BackFromGuide"', text)
                # Opening help is a local view change, not a fictitious API folder/tab.
                self.assertNotRegex(text, r'(?:folder|tab) = "guide"')

    def test_email_modes_have_distinct_instructions(self):
        personal = (GUIDES / "PersonalEmailGuide.razor").read_text()
        organization = (GUIDES / "OrganizationalEmailGuide.razor").read_text()
        self.assertIn('Id="personal-email-privacy"', personal)
        self.assertNotIn('Id="organization-email-official"', personal)
        self.assertIn('Id="organization-email-official"', organization)
        self.assertIn('if (personalMode)', (OFFICE / "Email.razor").read_text())

    def test_shared_mobile_and_print_support(self):
        css = (GUIDES / "OfficeGuide.razor.css").read_text()
        for rule in ['::deep .guide-hero', '::deep .guide-section', '@media (max-width: 640px)', '@media print']:
            self.assertIn(rule, css)
        template = (GUIDES / "OfficeGuide.razor").read_text()
        self.assertIn('officeGuides.scrollTo', template)
        self.assertIn('officeGuides.print', template)
        host_css = (CLIENT / "wwwroot/css/letters.css").read_text()
        self.assertIn('.ltx.guide-open .email-shell', host_css)
        self.assertIn('.ltx.guide-open .rail { display:none !important;', host_css)


if __name__ == "__main__":
    unittest.main()
