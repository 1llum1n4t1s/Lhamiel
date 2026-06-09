import json, sys, os
p = r'C:\Users\IMT\AppData\Local\Temp\claude\C--Users-IMT-dev-Lhamiel\0cfcc46d-ec5e-4d65-b446-61b7f26be234\tasks\wh8vc39ae.output'
d = json.load(open(p, 'r', encoding='utf-8'))
plan = d['result']['plan']
print('=== PLAN KEYS ===')
print(list(plan.keys()))
out = r'C:\Users\IMT\dev\Lhamiel\.claude-scratch\plan_dump.md'
with open(out, 'w', encoding='utf-8') as f:
    for k in ['ui_layout','settings_schema','persistence','compression_flow','i18n_keys','files_to_modify','tests_to_add','risks','open_questions']:
        f.write(f'\n\n## {k}\n\n')
        v = plan.get(k)
        if isinstance(v, list):
            for item in v:
                if isinstance(item, dict):
                    f.write('- ' + json.dumps(item, ensure_ascii=False) + '\n')
                else:
                    f.write('- ' + str(item) + '\n')
        else:
            f.write(str(v))
print('wrote', out, os.path.getsize(out))

# also dump critiques summary (just titles + severity)
crit_out = r'C:\Users\IMT\dev\Lhamiel\.claude-scratch\critiques_dump.md'
crits = d['result']['critiques']
with open(crit_out, 'w', encoding='utf-8') as f:
    for cat, body in crits.items():
        f.write(f'\n\n## {cat}\n\n')
        if isinstance(body, dict) and 'issues' in body:
            for issue in body['issues']:
                f.write(f"- **[{issue.get('severity','?')}]** {issue.get('title','')}\n")
                f.write(f"   - detail: {issue.get('detail','')[:400]}\n")
                f.write(f"   - fix: {issue.get('fix','')[:400]}\n")
print('wrote', crit_out, os.path.getsize(crit_out))

# investigations gotchas summary
inv_out = r'C:\Users\IMT\dev\Lhamiel\.claude-scratch\investigations_gotchas.md'
invs = d['result']['investigations']
with open(inv_out, 'w', encoding='utf-8') as f:
    for cat, body in invs.items():
        f.write(f'\n\n## {cat}\n\n')
        if isinstance(body, dict):
            for g in body.get('gotchas', []):
                f.write(f"- {g}\n")
print('wrote', inv_out, os.path.getsize(inv_out))
