import urllib.request
from html.parser import HTMLParser
import sys

class Stripper(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []
    def handle_data(self, data):
        if data.strip():
            self.parts.append(data.strip())

html = urllib.request.urlopen('https://code.visualstudio.com/docs/copilot/customization/mcp-servers', timeout=30).read().decode('utf-8')
parser = Stripper()
parser.feed(html)
text = '\n'.join(parser.parts)
sys.stdout.buffer.write(text.encode('utf-8'))
