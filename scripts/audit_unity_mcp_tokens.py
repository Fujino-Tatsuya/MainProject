"""Count actual captured JSON using named encodings; these are not provider billing totals."""
import json, os, pathlib, sys
out = pathlib.Path(__file__).resolve().parents[1] / 'output/unity-mcp-audit-2026-09-08'
sys.path.insert(0, str(out / 'python-deps'))
os.environ['TIKTOKEN_CACHE_DIR'] = str(out / 'tokenizer-cache')
import tiktoken
encodings = {name: tiktoken.get_encoding(name) for name in ('o200k_base', 'cl100k_base')}
rows=[]
for file in [out/'stdio-tools.json', out/'unslim-tools.json', *sorted(out.glob('map-*.json'))]:
    obj=json.loads(file.read_text(encoding='utf-8'))
    wire=json.dumps(obj, ensure_ascii=False, separators=(',', ':'))
    payload=obj['content'][0]['text'] if 'content' in obj else wire
    row={'file':file.name,'wireBytes':len(wire.encode()),'wireByteEstimate':round(len(wire.encode())/3.7),
         'tokens':{n:{'wire':len(e.encode(wire)), 'payload':len(e.encode(payload))} for n,e in encodings.items()}}
    rows.append(row)
(out/'token-results.json').write_text(json.dumps({'tiktoken':tiktoken.__version__, 'note':'Named tokenizer counts on captured JSON, not measured Codex/Claude billable tokens. MCP wrappers may be transformed by clients.', 'rows':rows},indent=2),encoding='utf-8')
print(json.dumps(rows,indent=2))
