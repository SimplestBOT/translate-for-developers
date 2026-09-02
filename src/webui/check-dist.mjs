// check-dist.mjs - 产物完整性检查：script 语法 + 中文文案 + 截断序列
import { readFileSync } from 'fs'

const html = readFileSync('dist/settings.html', 'utf8')
const m = html.match(/<script type="module"[^>]*>([\s\S]*?)<\/script>/)
if (!m) {
  console.log('NO-SCRIPT-FOUND')
  process.exit(1)
}
console.log('script-len=' + m[1].length)
try {
  new Function(m[1])
  console.log('SYNTAX-OK')
} catch (e) {
  console.log('SYNTAX-FAIL: ' + e.message)
}
for (const s of ['翻译热键', '更改热键', '百度翻译', '搜索语言', '已配置']) {
  console.log(s + '=' + html.includes(s))
}
const closing = m[1].includes('</script')
console.log('has-embedded-close-script=' + closing)
