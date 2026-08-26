import { CursorClickIcon as CursorClick } from "@phosphor-icons/react/dist/icons/CursorClick";
import { GraduationCapIcon as GraduationCap } from "@phosphor-icons/react/dist/icons/GraduationCap";
import { PencilSimpleIcon as PencilSimple } from "@phosphor-icons/react/dist/icons/PencilSimple";
import { X } from "@phosphor-icons/react/dist/icons/X";
import { Minus } from "@phosphor-icons/react/dist/icons/Minus";
import { Square } from "@phosphor-icons/react/dist/icons/Square";
import { Reveal } from "./Reveal";

export function NeverTakesOver() {
  return (
    <section className="py-20 sm:py-28 bg-[#ece9d8]/40 border-y border-[#d2d2d7]">
      <div className="mx-auto max-w-[1180px] px-5">
        <div className="text-center mb-12">
          <Reveal>
            <h2 className="max-w-[28ch] type-title text-ink font-pixel">
              It shows you how. It never does it for you.
            </h2>
            <p className="mt-4 mx-auto max-w-[62ch] type-body text-ink-muted">
              A tool that finishes the task for you cannot also be the thing that teaches you to do
              it. So Metis does not have that ability at all — the skill it leaves behind is yours.
            </p>
          </Reveal>
        </div>

        <Reveal delay={0.08}>
          <div className="grid gap-6 lg:grid-cols-3">
            
            {/* Card 1: Windows XP Setup Wizard - "It teaches while you work" */}
            <div className="xp-window flex flex-col h-full min-h-[460px] border-3 border-[#0054e3] shadow-[4px_4px_0_#000]">
              <div className="xp-titlebar">
                <span className="text-[12px] tracking-wide">Metis Companion Setup</span>
                <button className="xp-button-close"><X size={10} weight="bold" /></button>
              </div>

              {/* Wizard Content Layout */}
              <div className="flex-1 flex flex-col bg-[#ece9d8] text-black">
                {/* Main page content split */}
                <div className="flex-1 flex">
                  {/* Left branding banner (XP setup sidebar style) */}
                  <div className="w-1/3 bg-gradient-to-b from-[#1085d2] to-[#002f96] p-2 flex flex-col justify-between text-white border-r border-[#002f96]">
                    <div>
                      <div className="text-[14px] font-bold tracking-tight">Metis</div>
                      <div className="text-[9px] opacity-75">Desktop Assistant</div>
                    </div>
                    {/* XP Setup Image */}
                    <div className="border border-white/20 p-0.5 rounded overflow-hidden bg-black/30">
                      <img 
                        src="/image2.jpg" 
                        alt="Windows XP Setup" 
                        className="w-full h-auto max-h-[180px] object-cover"
                      />
                    </div>
                    <span className="text-[8px] opacity-50">Version 3.13.0</span>
                  </div>

                  {/* Right Wizard page */}
                  <div className="w-2/3 p-4 flex flex-col justify-between text-[11px]">
                    <div>
                      <h3 className="text-[14px] font-bold text-[#002375] mb-2">
                        Interactive Instruction
                      </h3>
                      <p className="text-zinc-700 leading-normal mb-3">
                        Metis explains what to do and why, in the application you are actually using, on the screen you are actually looking at.
                      </p>
                      <div className="win95-field bg-white p-2 text-[10px] text-zinc-600 rounded">
                        <div className="font-bold text-[#0054e3]">✓ Active Learning Path</div>
                        <div>Metis remembers what you learn per application and shortens advice as your skill increases.</div>
                      </div>
                    </div>

                    <div className="flex items-center gap-1.5 mt-4">
                      <GraduationCap size={18} className="text-blue-800" />
                      <span className="font-bold">Education Mode Active</span>
                    </div>
                  </div>
                </div>

                {/* Wizard Navigation Footer */}
                <div className="bg-[#ece9d8] border-t border-zinc-400 p-2.5 flex justify-end gap-1.5">
                  <button className="xp-button-win text-[11px] !px-3 !py-0.5" disabled>&lt; Back</button>
                  <button className="xp-button-win text-[11px] !px-3 !py-0.5">Next &gt;</button>
                  <div className="w-[1px] bg-zinc-400 mx-1.5" />
                  <button className="xp-button-win text-[11px] !px-3 !py-0.5">Cancel</button>
                </div>
              </div>
            </div>

            {/* Card 2: MS Paint - "It draws on your screen" */}
            <div className="paint-window flex flex-col h-full min-h-[460px] shadow-[4px_4px_0_#000] text-black">
              {/* Titlebar */}
              <div className="win95-titlebar">
                <span>metis_screen_draw.bmp - Paint</span>
                <div className="flex items-center gap-[3px]">
                  <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px]"><Minus /></button>
                  <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px]"><Square /></button>
                  <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px] !bg-[#cc3333] !text-white"><X /></button>
                </div>
              </div>

              {/* Menubar */}
              <div className="bg-[#dfdfdf] border-b border-[#7f7f7f] text-[11px] px-2 py-0.5 flex gap-3">
                <span><u>F</u>ile</span>
                <span><u>E</u>dit</span>
                <span><u>V</u>iew</span>
                <span><u>I</u>mage</span>
                <span><u>C</u>olors</span>
                <span><u>H</u>elp</span>
              </div>

              {/* Paint Work Area */}
              <div className="flex-1 flex bg-[#7f7f7f] p-1 overflow-hidden">
                {/* Left Tool Box */}
                <div className="w-10 bg-[#dfdfdf] border border-white border-r-[#7f7f7f] border-b-[#7f7f7f] p-0.5 grid grid-cols-2 gap-[1.5px] items-start content-start">
                  {[
                    "✏️", "🖌️", "🗑️", "🔍", 
                    "🎨", "📐", "📏", "✂️", 
                    "🔴", "⬜", "🔵", "📌"
                  ].map((tool, i) => (
                    <button 
                      key={i} 
                      className={`win95-button !p-0.5 text-[11px] h-[18px] flex items-center justify-center ${
                        i === 0 ? "border-top-[#7f7f7f] border-left-[#7f7f7f] border-right-white border-bottom-white bg-[#dfdfdf] shadow-inner" : ""
                      }`}
                    >
                      {tool}
                    </button>
                  ))}
                </div>

                {/* Paint Canvas */}
                <div className="flex-1 paint-canvas p-2 overflow-auto bg-[#808080] flex items-center justify-center">
                  <div className="bg-white border border-[#000] p-0.5 w-[210px] h-[270px]">
                    <img 
                      src="/image1.jpg" 
                      alt="Metis drawing overlays" 
                      className="w-full h-full object-cover"
                    />
                  </div>
                </div>
              </div>

              {/* Color Box */}
              <div className="bg-[#dfdfdf] border-t border-[#7f7f7f] p-2 flex gap-1 items-center justify-between text-[11px]">
                <div className="flex gap-[1.5px] border border-[#7f7f7f] p-0.5 bg-white">
                  <div className="w-4 h-4 bg-black" />
                  <div className="w-4 h-4 bg-white" />
                </div>
                <div className="flex flex-wrap gap-[1px] max-w-[200px]">
                  {["#000", "#555", "#f00", "#ff0", "#0f0", "#0ff", "#00f", "#f0f", "#800", "#880", "#080", "#088"].map((c) => (
                    <span 
                      key={c} 
                      className="w-3.5 h-3.5 border border-white" 
                      style={{ backgroundColor: c }} 
                    />
                  ))}
                </div>
                <div className="flex items-center gap-1">
                  <PencilSimple size={14} className="text-zinc-600 animate-pulse" />
                  <span className="font-pixel text-[10px]">Canvas</span>
                </div>
              </div>
            </div>

            {/* Card 3: Windows Task Manager - "It never takes the controls" */}
            <div className="win95-window flex flex-col h-full min-h-[460px] shadow-[4px_4px_0_#000] text-black">
              {/* Titlebar */}
              <div className="win95-titlebar">
                <span>Windows Task Manager</span>
                <button className="win95-button !p-0.5 h-4 w-4 flex items-center justify-center text-[9px]"><X /></button>
              </div>

              {/* Menu */}
              <div className="bg-[#c0c0c0] border-b border-[#808080] text-[11px] px-2 py-0.5 flex gap-3">
                <span><u>F</u>ile</span>
                <span><u>O</u>ptions</span>
                <span><u>V</u>iew</span>
                <span><u>H</u>elp</span>
              </div>

              {/* Task Manager Tabs */}
              <div className="bg-[#c0c0c0] px-1 pt-1 flex gap-[1px] border-b border-[#808080] text-[11px]">
                <span className="bg-[#c0c0c0] px-2 py-0.5 rounded-t border-t border-x border-white">Applications</span>
                <span className="bg-[#a0a0a0] text-zinc-800 px-2 py-0.5 rounded-t border-t border-x border-[#808080] cursor-pointer">Processes</span>
                <span className="bg-[#a0a0a0] text-zinc-800 px-2 py-0.5 rounded-t border-t border-x border-[#808080] cursor-pointer">Performance</span>
              </div>

              {/* Process List Area */}
              <div className="flex-1 bg-white p-3 flex flex-col justify-between text-[11px]">
                <div>
                  {/* Table headers */}
                  <div className="grid grid-cols-3 border-b border-[#808080] pb-1 text-[#444] font-bold font-pixel">
                    <span>Task</span>
                    <span>Status</span>
                    <span>Action Restrictions</span>
                  </div>
                  {/* Table items */}
                  <div className="divide-y divide-[#efefef] font-mono text-[10px] mt-1.5">
                    <div className="grid grid-cols-3 py-1 bg-blue-100 text-[#002375]">
                      <span className="font-bold">metis.exe</span>
                      <span className="text-[#008000]">Running (Safe)</span>
                      <span>Read-Only & Draw</span>
                    </div>
                    <div className="grid grid-cols-3 py-1">
                      <span>cursor_helper.dll</span>
                      <span>Idle</span>
                      <span>Pointer Guidance</span>
                    </div>
                    <div className="grid grid-cols-3 py-1 text-zinc-400">
                      <span>autopilot.exe</span>
                      <span className="text-[#d70015] font-bold">DISABLED</span>
                      <span>Clicking/Typing blocked</span>
                    </div>
                  </div>
                </div>

                {/* Screenshot inside task manager window */}
                <div className="win95-field bg-black my-2 p-0.5">
                  <img 
                    src="/image6.jpg" 
                    alt="Task Manager Safe Processes Visual" 
                    className="w-full h-24 object-cover"
                  />
                </div>

                {/* Details and End Task Button */}
                <div className="border-t border-[#808080] pt-2 flex items-center justify-between text-black">
                  <div className="flex items-center gap-1.5">
                    <CursorClick size={15} className="text-zinc-600" />
                    <span className="font-bold font-pixel">Never takes over</span>
                  </div>
                  <button className="win95-button text-[10px] !text-[#d70015]">End Task</button>
                </div>
              </div>
            </div>

          </div>
        </Reveal>
      </div>
    </section>
  );
}
