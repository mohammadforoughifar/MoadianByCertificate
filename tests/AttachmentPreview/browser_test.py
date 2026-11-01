"""Playwright integration: actual AttachmentBox eye -> modal -> JS -> real JWT/SQLite API.
Usage: pip install playwright; playwright install chromium;
       python browser_test.py http://127.0.0.1:5127 [results.json]
This is a test harness, not a mock HTML preview or a production deployment."""
import asyncio
import json
import sys
from pathlib import Path
from playwright.async_api import async_playwright, expect

URL = (sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5127').rstrip('/')
OUTPUT = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).with_name('BROWSER-RESULTS.json')

async def main():
    checks = []
    console_errors = []
    browser_errors = []
    auth = {'token': None}
    requests = []
    downloads = []
    def check(value, label):
        if not value: raise AssertionError(label)
        checks.append(label)
        print('PASS %d: %s' % (len(checks), label), flush=True)
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=['--no-sandbox', '--disable-dev-shm-usage'])
        context = await browser.new_context(viewport={'width': 1280, 'height': 900}, accept_downloads=True)
        await context.add_init_script('''(() => {const urls=new Set();const create=URL.createObjectURL.bind(URL),revoke=URL.revokeObjectURL.bind(URL);URL.createObjectURL=b=>{const u=create(b);urls.add(u);return u};URL.revokeObjectURL=u=>{urls.delete(u);return revoke(u)};window.__previewBlobCount=()=>urls.size})()''')
        page = await context.new_page()
        page.on('pageerror', lambda e: browser_errors.append(str(e)))
        page.on('console', lambda m: console_errors.append(m.text) if m.type == 'error' else None)
        page.on('download', lambda d: downloads.append(d.suggested_filename))
        def requested(r):
            requests.append(r.url)
            if '/api/attachments/preview' in r.url and r.headers.get('authorization'):
                auth['token'] = r.headers['authorization']
        page.on('request', requested)
        await page.goto(URL, wait_until='networkidle')
        await expect(page.locator('.attx-row')).to_have_count(22, timeout=30000)
        check(await page.locator('.attx-actions button[title="دانلود"]').count() == 0, 'read-only attachment list has no download buttons')
        check(await page.locator('.attx-actions button[title="حذف"]').count() == 0, 'read-only attachment list has no delete buttons')
        check(await page.locator('.attx-drop').count() == 0, 'read-only attachment list has no upload control')
        async def eye(name, expect_modal=True):
            row = page.locator('.attx-row').filter(has=page.locator('.attx-name', has_text=name)).filter(has=page.get_by_text(name, exact=True))
            # Match the exact filename so sample.pdf never selects protected/corrupt.pdf.
            row = page.locator('.attx-row').filter(has=page.locator('button.attx-name').filter(has_text=name))
            row = row.filter(has=page.get_by_role('button', name=name, exact=True))
            await row.locator('button[title="پیش‌نمایش"]').click()
            if expect_modal: await expect(page.locator('.fpv-dialog')).to_be_visible()
        async def close():
            if await page.locator('.fpv-dialog').count():
                await page.get_by_role('button', name='بستن پیش‌نمایش', exact=True).click()
            await expect(page.locator('.fpv-dialog')).to_have_count(0)
            await page.wait_for_function('filePreview.diagnostics().requests===0 && filePreview.diagnostics().pdfViewers===0 && filePreview.diagnostics().modalHandlers===0', timeout=15000)
        async def clean():
            await page.wait_for_function('__previewBlobCount()===0', timeout=15000)
        for name in ['sample.png', 'sample.jpg', 'sample.gif', 'sample.webp', 'sample.bmp', 'sample.tiff', 'sample.svg']:
            await eye(name)
            await expect(page.locator('img.fpv-img')).to_be_visible(timeout=15000)
            await page.wait_for_function("document.querySelector('img.fpv-img').complete && document.querySelector('img.fpv-img').naturalWidth>0")
            check(True, 'actual eye displays ' + name + ' (including disk/database and wrong MIME cases)')
            check(await page.locator('.fpv-head button[title="دانلود فایل"]').count() == 0, name + ' preview does not add download rights')
            await close(); await clean()
        await eye('sample.pdf')
        await expect(page.locator('.fpv-pdf-canvas')).to_be_visible(timeout=30000)
        await expect(page.locator('[data-pdf-pages]')).to_have_text('صفحه 1 از 3')
        check(await page.locator('iframe').count() == 0, 'PDF uses local canvas, not the browser PDF plugin')
        check(await page.locator('.fpv-pdf-canvas').evaluate('(c)=>c.width>100 && c.height>100'), 'PDF renders actual page pixels')
        await expect(page.get_by_title('صفحه بعدی')).to_be_enabled(timeout=15000)
        await page.get_by_title('صفحه بعدی').click()
        await expect(page.locator('[data-pdf-pages]')).to_have_text('صفحه 2 از 3')
        await expect(page.locator('.fpv-pdf-canvas')).to_have_attribute('aria-label', 'صفحه 2 PDF', timeout=15000)
        await expect(page.get_by_title('بزرگ‌نمایی')).to_be_enabled(timeout=15000)
        before = await page.locator('.fpv-pdf-canvas').evaluate('(c)=>c.width')
        await page.get_by_title('بزرگ‌نمایی').click()
        await expect(page.locator('.fpv-toolbar')).to_contain_text('125', timeout=15000)
        await page.wait_for_function('(width)=>document.querySelector(".fpv-pdf-canvas").width>width', arg=before, timeout=15000)
        await expect(page.get_by_title('بزرگ‌نمایی')).to_be_enabled(timeout=15000)
        after = await page.locator('.fpv-pdf-canvas').evaluate('(c)=>c.width')
        check(after > before, 'PDF next-page and zoom controls actually render')
        await page.keyboard.press('Escape')
        await expect(page.locator('.fpv-dialog')).to_have_count(0)
        await clean()
        check(True, 'Escape closes preview and revokes the PDF blob')
        for name, marker in [('sample.docx', 'Word content'), ('sample.xlsx', 'Excel text'), ('sample.pptx', 'PowerPoint slide 2'), ('sample.csv', 'quoted "word"')]:
            await eye(name)
            await expect(page.locator('iframe.fpv-frame')).to_be_visible(timeout=15000)
            check(await page.locator('iframe').get_attribute('sandbox') == '', name + ' frame grants neither scripts nor same-origin')
            frame = page.frame_locator('iframe.fpv-frame')
            await expect(frame.locator('body')).to_contain_text(marker, timeout=15000)
            check(True, 'actual eye displays content of ' + name)
            if name != 'sample.csv':
                await expect(frame.locator('img.embedded').first).to_be_visible()
                await expect(frame.locator('img.embedded').first).to_have_js_property('complete', True)
                check(await frame.locator('img.embedded').first.evaluate('(i)=>i.naturalWidth>0'), name + ' embedded image renders')
            check(await page.evaluate('window.PREVIEW_UNSAFE===undefined'), name + ' content cannot execute JavaScript in parent')
            await close(); await clean()
        await eye('sample.txt')
        await expect(page.locator('pre.fpv-text')).to_contain_text('Plain text preview')
        check(await page.locator('pre script').count() == 0, 'plain text markup is displayed, not executed')
        await close(); await clean()
        await eye('protected.pdf')
        await expect(page.locator('.fpv-pdf-password')).to_be_visible(timeout=30000)
        check(True, 'password-protected PDF requests file password rather than going blank')
        await page.locator('.fpv-pdf-password input').fill('wrong')
        await page.get_by_role('button', name='باز کردن PDF', exact=True).click()
        await expect(page.locator('.fpv-pdf-password')).to_contain_text('درست نیست', timeout=15000)
        await page.locator('.fpv-pdf-password input').fill('pdf-fixture')
        await page.get_by_role('button', name='باز کردن PDF', exact=True).click()
        await expect(page.locator('.fpv-pdf-canvas')).to_be_visible(timeout=30000)
        check(True, 'PDF file password retry opens the actual protected document')
        await close(); await clean()
        for name in ['legacy.doc', 'legacy.xls', 'legacy.ppt', 'corrupt.docx', 'corrupt.pdf', 'corrupt.jpg', 'unsupported.heic']:
            await eye(name)
            await expect(page.locator('.fpv-body [role="alert"]')).to_be_visible(timeout=20000)
            check(bool((await page.locator('.fpv-body [role="alert"]').inner_text()).strip()), 'clear, nonempty failure for ' + name)
            await close(); await clean()
        await eye('large.xlsx')
        frame = page.frame_locator('iframe.fpv-frame')
        await expect(frame.locator('body')).to_contain_text('250', timeout=15000)
        check(not await frame.locator('body').get_by_text('row-270', exact=True).count(), 'large worksheet limit disclosed without altering original file')
        await close(); await clean()
        check((await page.locator('[data-form-submits]').inner_text()).strip() == '0', 'eye/name/close controls do not submit their enclosing form')
        check(downloads == [], 'read-only previewing never initiates a browser download')
        # Real server enforcement, using the legitimate read-only JWT sent by the UI.
        check(bool(auth['token']), 'real browser preview sends Authorization header')
        headers = {'Authorization': auth['token']}
        for path in ['/api/attachments/download/1', '/api/attachments/1']:
            response = await (context.request.get(URL + path, headers=headers) if 'download/' in path else context.request.delete(URL + path, headers=headers))
            check(response.status == 403, 'real HTTP rejects read-only ' + ('download' if 'download/' in path else 'delete'))
        response = await context.request.post(URL + '/api/attachments/DocVersion/101', headers=headers, multipart={'file': {'name': 'forbidden.txt', 'mimeType': 'text/plain', 'buffer': b'forbidden'}})
        check(response.status == 403, 'real HTTP rejects read-only upload')
        response = await context.request.get(URL + '/api/attachments/preview/1')
        check(response.status == 401, 'real HTTP rejects anonymous preview')
        view_token = (await (await context.request.get(URL + '/__test/token/103')).json())['token']
        for path in ['/api/attachments/preview/1', '/api/attachments/preview-html/9', '/api/attachments/download/1']:
            response = await context.request.get(URL + path, headers={'Authorization': 'Bearer ' + view_token})
            check(response.status == 403, 'View-only JWT cannot open file content: ' + path)
        del view_token
        # HTTP failure messages must stay structured and must not become a blob/file.
        pattern = '**/api/attachments/preview/1'
        for status in [401, 403, 404]:
            async def fail(route, request=None, status=status):
                await route.fulfill(status=status, content_type='application/json', body=json.dumps({'message': 'آزمون خطای ' + str(status)}))
            await page.route(pattern, fail)
            await eye('sample.png')
            await expect(page.locator('.fpv-body [role="alert"]')).to_contain_text(str(status))
            check(True, 'UI surfaces structured HTTP ' + str(status) + ' without swallowing/retrying it')
            await close(); await clean(); await page.unroute(pattern, fail)
        async def spa(route):
            await route.fulfill(status=200, content_type='text/html', body='<html>wrong SPA response</html>')
        await page.route(pattern, spa)
        await eye('sample.png')
        await expect(page.locator('.fpv-body [role="alert"]')).to_contain_text('پاسخ نامعتبر')
        check(True, 'HTTP-200 login/SPA HTML is rejected instead of a broken image')
        await close(); await clean(); await page.unroute(pattern, spa)
        async def broken_network(route):
            await route.abort('failed')
        await page.route(pattern, broken_network)
        await eye('sample.png')
        await expect(page.locator('.fpv-body [role="alert"]')).to_contain_text('اتصال شبکه')
        check(True, 'network failure gives a clear retryable error without a blob')
        await close(); await clean(); await page.unroute(pattern, broken_network)
        # Closing during an in-flight fetch and immediately opening another file.
        release = asyncio.Event()
        async def delayed(route):
            await release.wait()
            try: await route.continue_()
            except Exception: pass
        await page.route('**/api/attachments/preview/8', delayed)
        await eye('sample.pdf')
        await close()
        release.set()
        await page.unroute('**/api/attachments/preview/8', delayed)
        await eye('sample.png')
        await expect(page.locator('.fpv-img')).to_be_visible()
        check((await page.locator('.fpv-head').inner_text()).find('sample.png') >= 0, 'late PDF response cannot overwrite a newly opened image')
        await close(); await clean()
        check(await page.evaluate('JSON.stringify(filePreview.diagnostics())') == '{"requests":0,"pdfViewers":0,"modalHandlers":0}', 'request/viewer/blob lifecycle leaves no stale preview state')
        # Confidential confirmation cache can expire while the page remains open.
        await context.request.post(URL + '/__test/confirm/2')
        await page.locator('[data-mode="secret"]').click()
        await expect(page.locator('.attx-row')).to_have_count(4)
        await eye('sample.docx')
        await expect(page.frame_locator('iframe').locator('.office-watermark')).to_contain_text('کاربر', timeout=15000)
        check(True, 'confidential Office preview carries server-generated viewer/time watermark')
        await close(); await clean()
        await context.request.post(URL + '/__test/expire/2')
        await eye('sample.docx', expect_modal=False)
        await expect(page.locator('.modal input[type="password"]')).to_be_visible(timeout=15000)
        check(await page.locator('.fpv-dialog').count() == 0, 'expired server confirmation closes preview and asks for password again')
        await page.locator('.modal input[type="password"]').fill('wrong')
        await page.get_by_role('button', name='تایید رمز', exact=True).click()
        await expect(page.locator('.modal')).to_contain_text('اشتباه')
        await page.locator('.modal input[type="password"]').fill('fixture-password')
        await page.get_by_role('button', name='تایید رمز', exact=True).click()
        await expect(page.locator('iframe.fpv-frame')).to_be_visible(timeout=15000)
        check(True, 'real password-confirm API and original confirmation modal reopen pending eye preview')
        await close(); await clean()
        await page.locator('[data-mode="watermark"]').click()
        await expect(page.locator('.attx-row')).to_have_count(1)
        check(await page.locator('.attx-tag-conf').count() == 0, 'non-confidential watermarked document is not mislabeled confidential')
        await eye('sample.docx')
        await expect(page.frame_locator('iframe').locator('.office-watermark')).to_contain_text('کاربر')
        check(True, 'watermark is inside Office response, not just a removable parent overlay')
        await close(); await clean()
        await page.locator('[data-mode="download"]').click()
        await expect(page.locator('.attx-row')).to_have_count(22)
        check(await page.locator('.attx-actions button[title="دانلود"]').count() == 22, 'independently granted download still appears')
        await eye('sample.png')
        await expect(page.locator('.fpv-img')).to_be_visible()
        check(await page.locator('.fpv-head button[title="دانلود فایل"]').count() == 1, 'authorized download remains available from the preview')
        async with page.expect_download() as download_info:
            await page.get_by_title('دانلود فایل', exact=True).click()
        download = await download_info.value
        check(download.suggested_filename == 'sample.png', 'preview download delegates to original protected download flow')
        await close()
        logs = await (await context.request.get(URL + '/__test/logs')).json()
        check(any(x['attachmentId'] == 9 and x['action'] == 'Preview' for x in logs), 'successful Office preview is logged on real API')
        check(any(x['attachmentId'] == 11 and x['action'] == 'Preview' for x in logs), 'successful PowerPoint preview is logged')
        check(any(x['attachmentId'] == 1 and x['action'] == 'Download' and x['userId'] == 102 for x in logs), 'authorized download log retains user identity')
        check(not any('access_token=' in u or 'token=' in u for u in requests if '/api/attachments/' in u), 'preview/download requests never put tokens in URLs')
        check(not any('preview-external.invalid' in u for u in requests), 'no external Office content requests')
        check(browser_errors == [], 'no uncaught browser JavaScript errors')
        # Chromium reports expected HTTP-4xx failures to its console; record without treating them as app exceptions.
        result = {'status': 'PASS', 'checks': len(checks), 'browser': browser.version, 'host': 'real source components + controllers; JWT/SQLite; Linux Kestrel, not IIS', 'passed': checks, 'uncaught_browser_errors': browser_errors, 'console_errors_including_expected_http_failures': console_errors}
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        OUTPUT.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
        await browser.close()
        print('ALL %d BROWSER PREVIEW CHECKS PASSED' % len(checks), flush=True)

if __name__ == '__main__': asyncio.run(main())
