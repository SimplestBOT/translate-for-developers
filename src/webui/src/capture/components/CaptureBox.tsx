// CaptureBox - 捕获框：capturing（三点呼吸脉冲）/ captured（键帽弹出，80ms 逐个入场）
// 外层盒子常驻（rise 入场 + breath 呼吸只跑一次，与 legacy #box 行为一致）
import { Fragment } from 'react'

interface Props {
  capturing: boolean
  keys: string[]
}

export default function CaptureBox(p: Props) {
  const pulse = p.capturing
  return (
    <div className={'bigbox box' + (pulse ? ' pulse' : '')}>
      {p.capturing
        ? [0, 1, 2].map((i) => (
            <span key={i} className="pdot" style={{ animationDelay: `${i * 160}ms` }} />
          ))
        : p.keys.map((k, i) => (
            <Fragment key={i}>
              {i > 0 && <span className="kcap plus">+</span>}
              <span className="kcap" style={{ ['--d' as string]: `${i * 80}ms` }}>
                {k}
              </span>
            </Fragment>
          ))}
    </div>
  )
}
