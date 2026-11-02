"""Read-only page/asset checks. Supply an origin; creates no matches."""
import html
from html.parser import HTMLParser
import re
import urllib.parse
import xml.etree.ElementTree as ET
import verify_web as web

class Links(HTMLParser):
    def __init__(self): super().__init__(); self.links = set(); self.symbols = set()
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        path = attrs.get('href') if tag in ('a', 'link', 'use') else attrs.get('src') if tag in ('script', 'img') else None
        if path and path.startswith('/'):
            self.links.add(urllib.parse.urldefrag(html.unescape(path))[0])
            if tag == 'use': self.symbols.add(urllib.parse.urldefrag(html.unescape(path)))

def main():
    browser = web.Browser()
    for path in ['/', '/History', '/Privacy', '/Error']:
        status, page, _ = browser.request(path); assert status == 200, (path, status)
        for forbidden in ["Use this page to detail", "Development Mode", "ASPNETCORE_ENVIRONMENT", "Exception:", "Password=", "Check PostgreSQL"]:
            assert forbidden not in page, (path, forbidden)
        parser = Links(); parser.feed(page)
        for target in parser.links:
            assert browser.request(target)[0] == 200, (path, target)
        for asset, symbol in parser.symbols:
            status, body, _ = browser.request(asset); assert status == 200
            ids = {node.attrib.get('id') for node in ET.fromstring(body).iter()}
            assert symbol in ids, (asset, symbol)
    assert browser.state() is None, 'Preview, help, data notice and history must not start a match'
    assert 'class="preview-cell' in browser.request('/')[1]
    assert browser.request('/lib/jquery/dist/jquery.min.js')[0] == 404
    print('PASS: all local page links/assets/SVG symbols load; no placeholder or production debug copy; preview/empty/history/data reads create no match')

if __name__ == '__main__': main()
