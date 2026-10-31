  const full = {screen:'reminder-default',settingsTab:'reminder',returnScreen:'reminder-default',selected:null,selectedTask:1,selectedLog:1,tag:'开发',note:'',tags:['开发','设计','测试','整理','文档'],saved:false,logsVisible:false,storage:'portable',calendarMonth:'2026-10',pendingImport:null,status:'界面预览 · 示例数据'};
  const catalog = [];
  const addScreens=(group,entries)=>entries.forEach(([key,label,page,tab])=>catalog.push({key,label,group,page,tab}));
  addScreens('主窗口',Object.entries(info).map(([page,value])=>['main-'+page,value[0],page]));
  addScreens('完整设置',[
    ['settings-reminder','提醒规则','settings','reminder'],['settings-time','时间与时段','settings','time'],['settings-tags','快捷标签','settings','tags'],['settings-prompts','提示词模板','settings','prompts'],['settings-data','数据与备份','settings','data']
  ]);
  addScreens('到时提醒',[
    ['reminder-default','到时弹窗 · 倒计时'],['reminder-logs','到时弹窗 · 展开今日流水'],['reminder-saved','到时弹窗 · 已保存反馈'],['reminder-empty','到时弹窗 · 空内容提示'],['reminder-todo','到时弹窗 · 已选择待办'],['reminder-finished','到时弹窗 · 活动时间结束'],['reminder-long','到时弹窗 · 长内容与多标签'],['reminder-multiscreen','多显示器同步提醒'],['access','临时收起 · 去其他应用']
  ]);
  addScreens('选择与编辑',[
    ['picker-todo','从待办选择'],['picker-history','从全部历史选择续记'],['calendar','选择有记录的日期'],['edit-todo','编辑待办'],['edit-log','编辑工作记录'],['edit-related','编辑同一事项的关联记录']
  ]);
  addScreens('启动与系统入口',[
    ['wizard','首次启动 · 数据位置'],['wizard-custom','首次启动 · 自定义目录'],['tray-counting','托盘右键菜单 · 计时中'],['tray-paused','托盘右键菜单 · 已暂停'],['tray-pause','托盘菜单 · 暂停时长'],['balloon','托盘操作结果提示'],['native-folder','系统选择文件夹'],['native-save','系统另存为']
  ]);
  addScreens('确认与操作结果',[
    ['confirm-unsaved','退出时有未保存内容'],['confirm-clear','清空草稿'],['confirm-delete-todo','删除待办'],['confirm-delete-log','删除工作记录'],['confirm-delete-report','删除报告'],['confirm-overwrite','同期已有报告'],['confirm-migrate','迁移到新数据目录'],['confirm-takeover','接管目录中已有的数据'],['success','保存、备份与导出成功'],['failure','保存、迁移或剪贴板失败'],['validation','必填与时间范围校验']
  ]);
  addScreens('内容状态',[
    ['empty-todos','待办为空'],['empty-history','工作记录为空'],['empty-reports','报告为空'],['empty-search','搜索无结果'],['long-content','长正文与长路径']
  ]);
  const reviewSelect=document.getElementById('tn-review-screen');
  let lastGroup='';
  reviewSelect.innerHTML=catalog.map(entry=>{const start=entry.group!==lastGroup;const prefix=start?(lastGroup?'</optgroup>':'')+`<optgroup label="${entry.group}">`:'';lastGroup=entry.group;return prefix+`<option value="${entry.key}">${entry.label}</option>`;}).join('')+'</optgroup>';
  document.getElementById('tn-review-count').textContent=catalog.length+' 个界面与状态';
  const extra=document.createElement('div');extra.className='tn-extra';extra.setAttribute('data-full-surface','');root.appendChild(extra);
  const fbutton=(label,action,primary=false,arg='')=>`<button type="button" class="tn-btn cursor-interaction${primary?' tn-btn-main':''}" data-f-action="${action}"${arg!==''?` data-f-arg="${esc(arg)}"`:''}${action==='wizard-folder'&&full.screen==='wizard'?' disabled':''}>${label}</button>`;
  const ftag=(selected='',name='')=>`<input class="tn-input" ${name?`name="${name}"`:''} list="tn-all-tags" value="${esc(selected)}" placeholder="选择或输入标签" aria-label="标签"><datalist id="tn-all-tags">${full.tags.map(t=>`<option value="${esc(t)}"></option>`).join('')}</datalist>`;
  const frow=(title,hint,value)=>`<div class="tn-settings-row"><div><h3>${title}</h3>${hint?`<p>${hint}</p>`:''}</div><div class="tn-settings-value">${value}</div></div>`;
  const fnumber=(name,value,min,max)=>`<input class="tn-input tn-width-number" type="number" name="${name}" value="${value}" min="${min}" max="${max}" aria-label="${name}" required>`;
  const ftime=(name,value)=>`<input class="tn-input" type="time" name="${name}" value="${value}" aria-label="${name}">`;
  const fcheck=(name,checked=true)=>`<input type="checkbox" name="${name}" aria-label="${name}" ${checked?'checked':''}>`;
  const oldSettingsView=settingsView;
  settingsView=function(){
    const tabs=[['reminder','提醒'],['time','时间与时段'],['tags','快捷标签'],['prompts','提示词模板'],['data','数据']];
    let body='';
    if(full.settingsTab==='reminder')body=`${frow('活动提醒周期','连续工作多久后提醒，5—480 分钟',fnumber('interval',45,5,480)+' 分钟')}${frow('活动时长','起身活动的建议时长，1—120 分钟',fnumber('duration',2,1,120)+' 分钟')}${frow('每次推迟时长','点击“推迟”后等待多久',fnumber('postpone',5,1,60)+' 分钟')}${frow('首次启动延迟','启动后先等待一段时间再提醒',fnumber('startup-delay',5,0,120)+' 分钟')}${frow('空闲检测','键盘和鼠标无操作时视为暂时离开',fcheck('启用空闲检测'))}${frow('空闲阈值','连续无操作多久后暂停计时',fnumber('idle-threshold',5,1,60)+' 分钟')}${frow('返回后的计时方式','根据自己的习惯继续剩余时间或重新计时','<div class="tn-radio-row"><label><input type="radio" name="idle-behavior" value="continue" checked>继续剩余时间</label><label><input type="radio" name="idle-behavior" value="restart">重新计时</label></div>')}${frow('全屏应用免打扰','演示、会议或全屏使用应用时延后提醒',fcheck('全屏应用免打扰'))}${frow('按 Esc 推迟提醒','在到时弹窗中按 Esc，按设置的时长推迟',fcheck('Esc 推迟提醒'))}${frow('提醒文案','{0} 会替换为活动分钟数','<input class="tn-input tn-settings-text" name="reminder-text" value="该起身活动 {0} 分钟了" aria-label="提醒文案">')}`;
    if(full.settingsTab==='time')body=`${frow('工作时间','只在设定的时间段内提醒',`<div class="tn-inline-times">${ftime('work-start','09:00')}<span>—</span>${ftime('work-end','18:00')}</div>`)}${frow('午休免打扰','启用后，午休期间暂停提醒',fcheck('启用午休免打扰'))}${frow('午休时间','这段时间内保持安静',`<div class="tn-inline-times">${ftime('break-start','12:00')}<span>—</span>${ftime('break-end','13:30')}</div>`)}${frow('一周起始日','用于确定周报的日期范围','<select class="tn-input" name="week-start" aria-label="一周起始日"><option>周一</option><option>周日</option></select>')}${frow('开机启动','登录 Windows 后自动运行 TickNote',fcheck('开机启动'))}`;
    if(full.settingsTab==='tags')body=`<div class="tn-form"><p class="tn-note">标签会出现在待办、记录和提醒弹窗中。提醒中再次点击已选标签可取消。</p><label class="tn-field">快捷标签<select class="tn-input tn-listbox" size="6" aria-label="快捷标签列表" data-f-tags>${full.tags.map(t=>`<option>${esc(t)}</option>`).join('')}</select></label><div class="tn-actions"><input class="tn-input" style="flex:1;min-width:100px" name="new-tag" placeholder="输入一个新标签" aria-label="新标签">${fbutton('添加','add-tag')}${fbutton('删除选中','remove-tag')}</div></div>`;
    if(full.settingsTab==='prompts')body=`<div class="tn-form"><label class="tn-field">日报提示词<span class="tn-note">可使用 {date}、{logs}、{stats}</span><textarea class="tn-template" name="daily-template">请根据 {date} 的工作记录，整理一份客观、简洁的日报。\n\n工作流水：\n{logs}\n\n统计：\n{stats}</textarea></label><label class="tn-field">周报提示词<span class="tn-note">可使用 {week}、{logs}、{daily_reports}、{stats}</span><textarea class="tn-template" name="weekly-template">请根据 {week} 的工作流水与日报，整理本周进展及下一步计划。\n\n{logs}\n\n{daily_reports}\n\n{stats}</textarea></label><div>${fbutton('恢复默认模板','reset-templates')}</div></div>`;
    if(full.settingsTab==='data')body=`<div class="tn-form"><div><h3>当前数据目录</h3><p class="tn-settings-path" style="margin:9px 0 14px">D:\\TickNoteData\\工作记录与报告</p><div class="tn-actions">${fbutton('打开数据目录','data-open')}${fbutton('更换数据位置…','data-change')}</div></div><div class="tn-form-foot"><h3>备份与导出</h3><p class="tn-note" style="margin:6px 0 14px">自动保留最近 7 份备份，也可以手动保存副本。</p><div class="tn-actions">${fbutton('立即备份','data-backup')}${fbutton('导出数据库副本…','data-export')}</div></div></div>`;
    return `<div class="tn-settings-tabs" aria-label="设置分类">${tabs.map(([key,label])=>`<button class="tn-tab cursor-interaction" type="button" data-f-action="settings-tab" data-f-arg="${key}" aria-pressed="${full.settingsTab===key}" aria-selected="${full.settingsTab===key}">${label}</button>`).join('')}</div><form class="tn-form" data-form="settings"><section class="tn-panel"><div class="tn-panel-content">${body}</div></section><div class="tn-actions tn-settings-bottom"><button type="submit" class="tn-btn tn-btn-main cursor-interaction">保存设置</button>${fbutton('还原已保存设置','reset-settings')}<span class="tn-note">保存后立即生效</span></div></form>`;
  };
  const oldManualView=manualView;
  manualView=function(){return oldManualView().replace('<label class="tn-field">这段时间做了什么？','<div>'+fbutton('从全部历史记录选择…','manual-history')+'</div><label class="tn-field">这段时间做了什么？').replace('<span class="tn-note">保存后会加入对应日期的工作流水</span>',fbutton('清空草稿','clear-draft')+'<span class="tn-note">保存后会加入对应日期的工作流水</span>');};
  const oldReminderView=reminderView;
  reminderView=function(){return oldReminderView()+`<div class="tn-actions" style="margin-top:18px">${fbutton('暂停 30 分钟','pause-time',false,'30')}${fbutton('暂停 1 小时','pause-time',false,'60')}${fbutton('暂停到今天下班','pause-time',false,'today')}</div>`;};
  const oldImportView=importView;
  importView=function(){return oldImportView().replace(btn('查看我的报告','go-reports'),fbutton('从剪贴板粘贴','paste-report')+btn('查看我的报告','go-reports'));};
  const oldAboutView=aboutView;
  aboutView=function(){return oldAboutView().replace('<div class="tn-form-foot">',`<div><h3>数据目录</h3><p class="tn-settings-path" style="margin:8px 0 12px">D:\\TickNoteData\\工作记录与报告</p>${fbutton('打开数据目录','data-open')}</div><div class="tn-form-foot">`);};
  const frameWindow=(title,body,foot,size='')=>`<section class="tn-window ${size}" role="dialog" aria-label="${esc(title)}"><div class="tn-dialog-top"><h2>${title}</h2><button type="button" class="tn-close cursor-interaction" aria-label="关闭窗口" data-f-action="back">×</button></div><div class="tn-window-body">${body}</div>${foot?`<div class="tn-window-foot">${foot}</div>`:''}</section>`;
  const desktop=(windowHTML,dim=false)=>`<div class="tn-desktop ${dim?'tn-desktop-dim':''}">${windowHTML}</div>`;
  function reminderCard(kind,small=false){
    const expanded=kind==='reminder-logs'||kind==='reminder-long'||full.logsVisible;
    const saved=kind==='reminder-saved'||full.saved;
    const finished=kind==='reminder-finished';
    const long=kind==='reminder-long';
    const selected=kind==='reminder-todo';
    const note=full.note||(selected?tasks.find(t=>t.id===full.selectedTask)?.text:long?'梳理跨显示器缩放与窗口对齐问题，逐项检查长标题、长标签、长路径和报告正文在不同窗口尺寸下的显示效果。\n记录了从 100% 缩放显示器拖到 150% 显示器后需要核对的项目，下一步继续检查输入框与底部操作区。':'');
    const tags=long?[...full.tags,'跨显示器兼容性与布局检查','会议纪要与需求确认']:full.tags;
    const logs=expanded?`<div class="tn-log-preview">${records.slice(0,small?1:3).map(r=>`<div class="tn-log-item"><div class="tn-note">${r.start} — ${r.end} · ${esc(r.tag)}</div><p>${esc(r.text)}</p><div class="tn-actions">${fbutton('继续','continue-record',false,r.id)}${fbutton('编辑','edit-record',false,r.id)}${fbutton('复制','copy-record',false,r.id)}${fbutton('删除','delete-record',false,r.id)}</div></div>`).join('')}</div>`:'';
    const body=`<div class="tn-break-head"><div><h2>${long?'休息一下，让眼睛和身体都放松两分钟':'该起身活动一下了'}</h2><p class="tn-note">站起来走走，喝口水，让眼睛也休息一下。</p></div></div><div class="tn-break-time"><strong>${finished?'00:00':'01:32'}</strong><div class="tn-progress" role="progressbar" aria-label="活动倒计时进度" aria-valuenow="${finished?100:23}" aria-valuemin="0" aria-valuemax="100"><div style="width:${finished?100:23}%"></div></div></div>${selected?'<div class="tn-context">来自待办 · 已带入内容和标签，待办状态保持不变</div>':''}<label class="tn-field">这段时间在做什么？<textarea class="tn-break-note" data-f-note placeholder="顺手记一句，之后写日报更轻松。">${esc(saved?'':note)}</textarea></label>${kind==='reminder-empty'?'<p class="tn-error" role="alert">先写一点工作内容，再保存记录。</p>':''}<div class="tn-break-tags">${tags.map(t=>`<button type="button" class="tn-tag-button cursor-interaction" data-f-action="toggle-tag" data-f-arg="${esc(t)}" aria-pressed="${full.tag===t}">${esc(t)}</button>`).join('')}<span class="tn-spacer"></span><button type="button" class="tn-btn tn-btn-main cursor-interaction" data-f-action="save-reminder" ${saved?'disabled':''}>${saved?'已保存 ✓':'保存记录'}</button></div><div class="tn-break-tools"><span class="tn-note">今天已记录 · ${saved?5:4} 条</span>${fbutton('全部历史续记','pick-history')}${fbutton('从待办选择','pick-todo')}${fbutton(expanded?'隐藏今日':'显示今日','toggle-logs')}</div>${logs}${fbutton('去其他应用复制 / 查看','release-reminder')}`;
    const foot=`<div style="width:100%"><div class="tn-full-actions">${fbutton(finished?'刚活动完 ✓':'刚活动完','finish-break',true)}${fbutton('跳过','skip-break')}${fbutton('推迟 5 分钟','postpone-break')}</div><p class="tn-note" style="margin-top:10px">${finished?'活动时间已到，点击“刚活动完”继续工作。':'倒计时仅作建议；活动过后可直接点击“刚活动完”。'}</p></div>`;
    return `<section class="tn-window" aria-label="到时间的起身活动提醒"><div class="tn-window-body">${body}</div><div class="tn-window-foot">${foot}</div></section>`;
  }
  function pickerView(kind){
    const todo=kind==='picker-todo';
    const list=todo?tasks.filter(t=>!t.done):records;
    const title=todo?'从待办选择正在做的事':'从全部历史记录选择事项';
    const controls=todo?`<label class="tn-field">日期范围<select class="tn-input" data-f-pick-date><option value="">全部日期</option><option value="${TODAY}">${TODAY}</option><option value="2026-10-29">2026-10-29</option></select></label>`:`<div class="tn-actions">${fbutton('记录日历 ▾','picker-calendar')}${fbutton('查看全部','picker-all')}</div>`;
    const body=`<p class="tn-note">${todo?'选择后将内容和标签填入提醒草稿，不会完成或修改待办。':'选择一条历史记录继续填写，保留关联关系，方便以后同步修改。'}</p><div class="tn-form-grid">${controls}<label class="tn-field">搜索<input type="search" class="tn-input" placeholder="${todo?'内容、标签或日期':'工作内容或标签'}" data-f-pick-search></label></div><div class="tn-select-list" data-f-pick-list>${list.map(item=>`<label class="tn-select-row" data-f-pick-row data-date="${item.date}" data-search="${esc((item.text||'')+item.tag+item.date)}"><input type="radio" name="full-pick" value="${item.id}" ${String(full.selected)===String(item.id)?'checked':''}><span>${esc(item.text)}<small>${item.date} ${todo?'':item.start+' — '+item.end} · ${esc(item.tag||'无标签')}</small></span></label>`).join('')}</div>${!todo?'<div class="tn-actions"><span class="tn-note">选中内容预览</span><span class="tn-spacer"></span>'+fbutton('复制选中内容','copy-picked')+'</div><textarea class="tn-select-preview" readonly aria-label="选中历史记录全文" data-f-pick-preview placeholder="选择一条记录查看全文"></textarea>':''}<span class="tn-note" data-f-pick-count>${list.length} 项</span>`;
    const footer=fbutton('取消','back')+`<button type="button" class="tn-btn tn-btn-main cursor-interaction" data-f-action="accept-pick" ${full.selected===null?'disabled':''}>${todo?'使用这项待办':'继续这项记录'}</button>`;
    return desktop(frameWindow(title,body,footer,'tn-window-wide'));
  }
  function calendarView(){
    const [year,month]=full.calendarMonth.split('-').map(Number),start=(new Date(year,month-1,1).getDay()+6)%7,days=new Date(year,month,0).getDate();
    let cells=Array(start).fill('<span class="tn-month-empty"></span>').join('');
    for(let day=1;day<=days;day++){const date=`${year}-${String(month).padStart(2,'0')}-${String(day).padStart(2,'0')}`,available=records.some(r=>r.date===date);cells+=`<button type="button" class="cursor-interaction" ${available?'':'disabled'} data-f-action="calendar-day" data-f-arg="${date}" aria-label="${date}${available?'，有记录':'，无记录'}" aria-pressed="${date===TODAY}">${day}</button>`;}
    const body=`<div class="tn-calendar-head">${fbutton('‹','calendar-prev')}<select class="tn-input" data-f-month aria-label="月份"><option value="2026-10" ${month===10?'selected':''}>2026 年 10 月</option><option value="2026-09" ${month===9?'selected':''}>2026 年 9 月</option></select>${fbutton('›','calendar-next')}</div><div class="tn-calendar">${['一','二','三','四','五','六','日'].map(x=>`<span>${x}</span>`).join('')}${cells}</div><p class="tn-note">蓝色日期有记录；灰色日期不可选择。</p>`;
    return desktop(frameWindow('选择有记录的日期',body,fbutton('取消','back'),'tn-window-sm'));
  }
  function editView(kind){
    const todo=kind==='edit-todo',related=kind==='edit-related';
    const item=todo?(tasks.find(t=>t.id===full.selectedTask)||tasks[0]):(records.find(r=>r.id===full.selectedLog)||records[0]);
    const date=todo?`<p class="tn-note">所属日期：${item.date}</p>`:`<div class="tn-form-grid"><label class="tn-field">工作日期<input class="tn-input" type="date" value="${item.date}" data-f-edit-date></label><label class="tn-field">标签${ftag(item.tag)}</label><label class="tn-field">开始时间<input class="tn-input" type="time" value="${item.start}" data-f-edit-start></label><label class="tn-field">结束时间<input class="tn-input" type="time" value="${item.end}" data-f-edit-end></label></div>`;
    const body=`${date}${todo?'<label class="tn-field">标签（可选）'+ftag(item.tag)+'</label>':''}<label class="tn-field">${todo?'待办内容':'工作内容'}<textarea rows="5" data-f-edit-content>${esc(item.text)}</textarea></label>${!todo?`<div><label class="tn-check"><input type="checkbox" data-f-sync ${related?'checked':''} ${related?'':'disabled'}>同步修改同一事项（共 ${related?3:1} 条，含当前记录）</label><p class="tn-note" style="margin-top:6px">同步内容和标签；日期、时间只修改当前记录。</p></div>`:''}<p class="tn-error" data-f-edit-error hidden role="alert"></p>`;
    return desktop(frameWindow(todo?'编辑待办':'编辑工作记录',body,fbutton('取消','back')+fbutton('保存修改','save-edited',true,todo?'todo':'log'),todo?'tn-window-sm':''));
  }
  const messages={
    'confirm-unsaved':{title:'有未保存的内容',text:'“我的报告”中有尚未保存的修改。\n是否保存后继续退出？',buttons:[['取消','back'],['放弃修改','discard'],['保存并继续','ack',true]]},
    'confirm-clear':{title:'清空草稿',text:'确定清空尚未保存的草稿？\n清空后无法恢复这次输入。',buttons:[['取消','back'],['清空草稿','confirm-clear',true]]},
    'confirm-delete-todo':{title:'删除待办',text:'确定删除这条待办？删除后不可恢复。',detail:'确认新版本的界面布局与交互流程',buttons:[['取消','back'],['删除待办','confirm-delete',true,'todo']]},
    'confirm-delete-log':{title:'删除工作记录',text:'确定删除这条工作记录？',detail:'14:00 — 14:45　整理本周完成事项，归纳报告内容。',buttons:[['取消','back'],['删除记录','confirm-delete',true,'log']]},
    'confirm-delete-report':{title:'删除报告',text:'确定删除 2026-10-30 的这份日报？',buttons:[['取消','back'],['删除报告','confirm-delete',true,'report']]},
    'confirm-overwrite':{title:'同期已有报告',text:'2026-10-30 已有 1 份日报。\n可以覆盖最新一份，也可以保留两份。',buttons:[['取消','back'],['保留两份','import-new'],['覆盖最新一份','import-overwrite',true]]},
    'confirm-migrate':{title:'更换数据位置',text:'将把当前数据完整迁移到新目录，迁移前自动备份。旧目录保留作为兜底。\n完成后软件会重新启动。',detail:'当前：D:\\TickNoteData\\工作记录与报告\n新位置：E:\\我的工作资料\\TickNote',buttons:[['取消','back'],['迁移并重启','simulate-migrate',true]]},
    'confirm-takeover':{title:'检测到已有数据',text:'这个目录中已经有 TickNote 数据。\n继续后会直接使用该目录中的设置、工作流水与报告，当前数据不会迁入这个目录。',detail:'E:\\我的工作资料\\TickNote',buttons:[['取消','back'],['使用已有数据','simulate-migrate',true]]},
    'success':{title:'备份成功',text:'数据已备份完成。',detail:'D:\\TickNoteData\\工作记录与报告\\backup\\2026-10-30\\data.db',buttons:[['确定','back',true]]},
    'failure':{title:'保存失败',text:'当前操作未完成，请检查后重试。\n你的输入仍然保留。',detail:'示例错误：目标目录暂时无法写入，请检查目录权限或可用空间。',buttons:[['确定','back',true]]}
  };
  function messageView(key){const m=messages[key];return desktop(frameWindow(m.title,`<div class="tn-message"><span class="tn-message-symbol" aria-hidden="true">${key==='failure'?'!':key==='success'?'✓':'?'}</span><p>${esc(m.text)}</p></div>${m.detail?`<div class="tn-message-detail">${esc(m.detail)}</div>`:''}`,m.buttons.map(([label,action,primary,arg])=>fbutton(label,action,primary,arg??'')).join(''),'tn-window-sm'));}
  function wizardView(custom){const body=`<div><h2 style="font-size:22px">欢迎使用 TickNote</h2><p class="tn-note" style="margin-top:7px">选择记录、设置和报告的保存位置，以后可以在设置中更换。</p></div><label class="tn-wizard-option"><input type="radio" name="full-storage" value="portable" ${custom?'':'checked'}><div><h3>便携模式</h3><p>保存在软件旁的 data 文件夹，复制整个文件夹即可迁移。</p><div class="tn-settings-path">D:\\TickNote\\data</div></div></label><label class="tn-wizard-option"><input type="radio" name="full-storage" value="custom" ${custom?'checked':''}><div><h3>自定义目录</h3><p>选择一个便于备份和长期保存的文件夹。</p><div class="tn-actions"><input class="tn-input" style="flex:1;min-width:100px" name="wizard-path" value="${custom?'E:\\我的工作资料\\TickNote':''}" placeholder="请选择保存目录" ${custom?'':'disabled'} aria-label="自定义数据目录">${fbutton('浏览…','wizard-folder')}</div></div></label><p class="tn-note">关闭此窗口会退出程序，不保存本次选择。</p>`;return desktop(frameWindow('TickNote · 首次运行设置',body,fbutton('退出','back')+fbutton('开始使用','wizard-start',true)));}
  function accessView(){return desktop(frameWindow('提醒已收起 · 可切换其他应用',`<p class="tn-note">输入已保留，可以切换到其他应用查看或复制内容。</p><textarea class="tn-select-preview" readonly aria-label="已保留的提醒草稿">${esc(full.note||'确认新版本的界面布局与交互流程，补充缩放与对齐检查项。')}</textarea>`,`<div class="tn-full-actions" style="width:100%">${fbutton('复制当前内容','copy-draft')}${fbutton('查看全部历史','pick-history')}${fbutton('返回提醒','restore-reminder',true)}</div>`,'tn-window-sm'));}
  function trayView(kind){const paused=kind==='tray-paused',submenu=kind==='tray-pause';const menuItem=(label,action,arg='',expanded=false)=>`<button type="button" class="tn-menu-row cursor-interaction" data-f-action="${action}" data-f-arg="${arg}" ${expanded?'aria-expanded="true"':''}>${label}</button>`;const menu=`<div class="tn-system-menu" role="menu" aria-label="TickNote 托盘菜单"><div class="tn-menu-label">TickNote v1.0.3</div><hr class="tn-menu-divider">${menuItem('打开主界面','tray-page','todos')}${menuItem('查看今日','tray-page','history')}${menuItem('每日待办','tray-page','todos')}${menuItem('历史流水','tray-page','history')}${menuItem('补记一条','tray-page','manual')}${menuItem('我的报告','tray-page','reports')}<hr class="tn-menu-divider">${menuItem('生成日报（复制提示词）','tray-copy','daily')}${menuItem('生成周报（复制提示词）','tray-copy','weekly')}${menuItem('导入报告','tray-page','import')}${menuItem('导出原始流水…','tray-page','export')}<hr class="tn-menu-divider">${menuItem('暂停提醒 <span class="tn-spacer"></span> ›','tray-pause','',submenu)}${menuItem('恢复提醒','tray-resume')}<hr class="tn-menu-divider">${menuItem('设置','tray-page','settings')}${menuItem('关于','tray-page','about')}<hr class="tn-menu-divider">${menuItem('退出','tray-exit')}</div>`;const sub=submenu?`<div class="tn-system-menu tn-submenu" role="menu" aria-label="暂停时长">${menuItem('30 分钟','pause-time','30')}${menuItem('1 小时','pause-time','60')}${menuItem('今天下班前','pause-time','today')}</div>`:'';return `<div class="tn-screen-caption">${paused?'TickNote v1.0.3 · 提醒已暂停':'TickNote v1.0.3 · 距下次提醒 18 分 42 秒'}</div><div class="tn-desktop"><div class="tn-tray-group">${menu}${sub}</div></div>`;}
  function nativeView(save){const body=`<div class="tn-actions"><span class="tn-note">此电脑 › ${save?'文档':'我的工作资料'}</span><span class="tn-spacer"></span><input class="tn-input tn-width-small" placeholder="搜索当前文件夹" aria-label="搜索文件夹"></div><div class="tn-native-browser"><aside class="tn-native-sidebar"><span>桌面</span><span>文档</span><span>下载</span><span>此电脑</span></aside><div class="tn-native-files"><button class="tn-native-file cursor-interaction" type="button" data-f-action="native-select" aria-selected="true">▸ TickNote</button><button class="tn-native-file cursor-interaction" type="button" data-f-action="native-select" aria-selected="false">▸ 工作记录与报告</button><button class="tn-native-file cursor-interaction" type="button" data-f-action="native-select" aria-selected="false">▸ 归档</button></div></div>${save?'<label class="tn-field">文件名<input class="tn-input" value="工作流水_2026-10-26_2026-10-30.md" name="native-filename"></label><label class="tn-field">保存类型<select class="tn-input" aria-label="保存类型"><option>Markdown 文档（*.md）</option><option>CSV 文件（*.csv）</option><option>SQLite 数据库（*.db）</option></select></label>':'<label class="tn-field">文件夹<input class="tn-input" value="E:\\我的工作资料\\TickNote" name="native-folder"></label>'}`;return desktop(frameWindow(save?'另存为':'选择数据保存目录',body,fbutton('取消','back')+fbutton(save?'保存':'选择文件夹','native-accept',true,save?'save':'folder'),'tn-window-wide'));}
  function emptyView(key){const content={
    'empty-todos':['每日待办','还没有待办事项','记下一件要做的事，完成后勾选即可。','添加待办','main-todos'],
    'empty-history':['历史流水','这一天还没有工作记录','可以补记一条，或者查看其他有记录的日期。','补记一条','main-manual'],
    'empty-reports':['我的报告','还没有保存的报告','准备工作记录给 AI，生成后粘贴回来保存。','生成报告','main-generate'],
    'empty-search':['历史流水','没有找到相关记录','试试其他关键词，或清空搜索后查看全部。','清空搜索','main-history']
  }[key];return `<div class="tn-desktop"><section class="tn-blank-page"><h2>${content[0]}</h2><div class="tn-blank-box"><h3>${content[1]}</h3><p>${content[2]}</p>${fbutton(content[3],'screen',true,content[4])}</div></section></div>`;}
  function validationView(){return desktop(frameWindow('补记一条',`<label class="tn-field">工作内容<textarea rows="4" class="tn-invalid" aria-invalid="true" placeholder="写下这段时间做了什么"></textarea><span class="tn-error" role="alert">工作内容不能为空。</span></label><div class="tn-form-grid"><label class="tn-field">开始时间<input class="tn-input" type="time" value="15:00"></label><label class="tn-field">结束时间<input class="tn-input tn-invalid" type="time" value="14:00" aria-invalid="true"><span class="tn-error">结束时间应晚于开始时间。</span></label></div>`,fbutton('取消','back')+fbutton('返回修改','screen',true,'main-manual')));}
  function longContentView(){return desktop(frameWindow('工作记录全文',`<p class="tn-note">2026-10-30 · 14:00 — 14:45 · 设计</p><textarea readonly rows="9" aria-label="长工作记录全文">${esc(('检查多屏显示下的缩放与布局问题，核对主窗口、提醒弹窗、选择窗口和设置页面的文字、控件与操作区域。\n').repeat(8))}</textarea><label class="tn-field">数据目录<textarea rows="3" readonly>D:\\我的工作资料\\项目归档\\2026年度\\研发团队\\日报与周报\\TickNote本地记录和自动备份\\data</textarea></label>`,fbutton('复制全文','copy-long')+fbutton('关闭','back',true),'tn-window-wide'));}
  function fullSurfaceHTML(key){
    if(key==='reminder-multiscreen')return `<div class="tn-monitor-pair">${['主显示器','第二显示器'].map(label=>`<section class="tn-monitor"><div class="tn-screen-caption">${label}</div><div class="tn-desktop tn-desktop-dim">${reminderCard('reminder-default',true)}</div></section>`).join('')}</div>`;
    if(key.startsWith('reminder-'))return desktop(reminderCard(key),true);
    if(key==='access')return accessView();
    if(key.startsWith('picker-'))return pickerView(key);
    if(key==='calendar')return calendarView();
    if(key.startsWith('edit-'))return editView(key);
    if(messages[key])return messageView(key);
    if(key==='wizard'||key==='wizard-custom')return wizardView(key==='wizard-custom');
    if(key.startsWith('tray-'))return trayView(key);
    if(key==='balloon')return `<div class="tn-desktop"><section class="tn-toast" role="status"><span class="tn-mark" aria-hidden="true">✓</span><div><h3>TickNote</h3><p>提示词和工作记录已复制，可以粘贴给 AI。</p></div></section></div>`;
    if(key==='native-folder'||key==='native-save')return nativeView(key==='native-save');
    if(key.startsWith('empty-'))return emptyView(key);
    if(key==='validation')return validationView();
    if(key==='long-content')return longContentView();
    return '';
  }
  full.stack=[];
  const oldNotify=notify;
  notify=function(message){oldNotify(message);full.status=message;const result=root.querySelector('[data-full-status]');if(result)result.textContent=message;};
  function captureFull(){const note=root.querySelector('[data-f-note]');if(note)full.note=note.value;}
  function updateReviewSelect(){const key=full.screen.startsWith('main-')?'main-'+state.page:full.screen;if([...reviewSelect.options].some(o=>o.value===key))reviewSelect.value=key;}
  const oldRender=render;
  render=function(){oldRender();const main=full.screen.startsWith('main-')||full.screen.startsWith('settings-');root.dataset.fullMode=main?'main':'extra';if(main){extra.innerHTML='';if(full.screen.startsWith('main-'))full.screen='main-'+state.page;if(state.page==='history'){const toolbar=root.querySelector('.tn-toolbar');if(toolbar&&!toolbar.querySelector('[data-f-action=main-calendar]'))toolbar.insertAdjacentHTML('beforeend',fbutton('记录日历…','main-calendar'));}if(state.page==='todos'){const title=root.querySelector('[data-task-count]')?.parentElement;if(title?.firstChild)title.firstChild.textContent=state.showCompleted?'全部事项':'未完成事项';}}else{extra.innerHTML=fullSurfaceHTML(full.screen)+`<div class="tn-full-status" data-full-status role="status" aria-live="polite">${esc(full.status)}</div>`;}updateReviewSelect();refreshIcons();};
  function showFull(key,push=true){if(!catalog.some(x=>x.key===key))return;captureFull();captureDraft();if(push&&full.screen!==key)full.stack.push(full.screen);full.screen=key;const entry=catalog.find(x=>x.key===key);if(entry.page){state.page=entry.page;if(entry.tab)full.settingsTab=entry.tab;}render();saveState();}
  function backFull(){showFull(full.stack.pop()||'main-reminder',false);}
  function openPicker(todo){full.selected=null;showFull(todo?'picker-todo':'picker-history');}
  function filterPicker(){const query=root.querySelector('[data-f-pick-search]')?.value||'',date=root.querySelector('[data-f-pick-date]')?.value||'';let visible=0;root.querySelectorAll('[data-f-pick-row]').forEach(row=>{row.hidden=!!((query&&!row.dataset.search.includes(query))||(date&&row.dataset.date!==date));if(!row.hidden)visible++;});const count=root.querySelector('[data-f-pick-count]');if(count)count.textContent=visible+' 项';}
  const oldSaveState=saveState;
  saveState=function(){if(window.openai?.setWidgetState)window.openai.setWidgetState({modelContent:{screen:catalog.find(x=>x.key===full.screen)?.label||info[state.page][0],accent:design.accent,density:design.compact?'紧凑':'舒适'},privateContent:{page:state.page,design,screen:full.screen,settingsTab:full.settingsTab}}).catch(()=>{});};
  const oldRestoreHost=restoreHost;
  restoreHost=function(saved){oldRestoreHost(saved);const p=saved?.privateContent;if(p?.screen&&catalog.some(x=>x.key===p.screen))full.screen=p.screen;if(p?.settingsTab)full.settingsTab=p.settingsTab;};
  reviewSelect.addEventListener('change',()=>{full.stack=[];full.saved=false;full.logsVisible=false;full.selected=null;full.note='';full.status='界面预览 · 示例数据';showFull(reviewSelect.value,false);});
  actions['preview-reminder']=()=>{full.saved=false;showFull('reminder-default');};
  editTask=function(id){full.selectedTask=id;showFull('edit-todo');};
  root.addEventListener('click',event=>{
    const button=event.target.closest('button');if(!button)return;
    if(button.dataset.editRecord){event.preventDefault();event.stopImmediatePropagation();full.selectedLog=Number(button.dataset.editRecord);showFull('edit-log');return;}
    if(button.dataset.page){full.screen='main-'+button.dataset.page;}
    if(button.dataset.action?.startsWith('go-'))full.screen='main-'+button.dataset.action.slice(3);
  },true);
  tagOptions=function(selected=''){return ['',...new Set([...full.tags,...(selected?[selected]:[])])].map(tag=>`<option value="${esc(tag)}" ${tag===selected?'selected':''}>${esc(tag||'无标签')}</option>`).join('');};
  const fCopy=(text,message='内容已复制')=>{if(navigator.clipboard?.writeText)navigator.clipboard.writeText(text).then(()=>notify(message)).catch(()=>{messages.failure.title='复制失败';messages.failure.text='剪贴板正被其他程序占用，请稍后重试。\n当前内容仍然保留。';messages.failure.detail='';showFull('failure');});else notify('复制演示：'+message);};
  function completeImport(overwrite){const pending=full.pendingImport;if(!pending){backFull();notify(overwrite?'覆盖报告（演示）':'保留两份报告（演示）');return;}let item=overwrite?reports.find(r=>r.type===pending.type&&r.date===pending.date):null;if(item){item.body=pending.body;}else{item={id:'import-'+Date.now(),type:pending.type,date:pending.date,title:pending.date,body:pending.body};reports.unshift(item);}state.reportId=item.id;state.reportType=item.type;full.pendingImport=null;const field=root.querySelector('[name=import-body]');if(field)field.value='';delete state.drafts['import:import-body'];full.stack=[];showFull('main-reports',false);notify('示例报告已保存');}
  actions['export-records']=()=>{const from=root.querySelector('[name=export-start]').value,to=root.querySelector('[name=export-end]').value;if(from>to){notify('开始日期不能晚于结束日期');return;}showFull('native-save');};
  const faction={
    back:backFull,
    screen:arg=>showFull(arg),
    'settings-tab':arg=>{captureDraft();full.settingsTab=arg;full.screen='settings-'+arg;render();saveState();},
    'add-tag':()=>{const value=root.querySelector('[name=new-tag]').value.trim();if(!value){notify('请先输入标签名称');return;}if(!full.tags.includes(value))full.tags.push(value);captureDraft();state.drafts['settings:new-tag']='';render();notify('标签已加入，保存设置后生效（演示）');},
    'remove-tag':()=>{const selected=root.querySelector('[data-f-tags]').value;if(!selected){notify('请选择一个标签');return;}full.tags=full.tags.filter(t=>t!==selected);captureDraft();render();notify('标签已移除，保存设置后生效（演示）');},
    'reset-settings':()=>{Object.keys(state.drafts).filter(k=>k.startsWith('settings:')).forEach(k=>delete state.drafts[k]);Object.assign(state.drafts,full.savedSettings||{});full.tags=[...(full.savedTags||['开发','设计','测试','整理','文档'])];render();notify('已还原已保存设置（演示）');},
    'reset-templates':()=>{delete state.drafts['settings:daily-template'];delete state.drafts['settings:weekly-template'];render();notify('已恢复默认模板，保存设置后生效（演示）');},
    'data-open':()=>notify('打开数据目录（演示，不操作系统）'),
    'data-change':()=>showFull('native-folder'),
    'data-backup':()=>{messages.success.title='备份成功';messages.success.text='数据已备份完成。';messages.success.detail='D:\\TickNoteData\\工作记录与报告\\backup\\2026-10-30\\data.db';showFull('success');},
    'data-export':()=>showFull('native-save'),
    'paste-report':()=>{const target=root.querySelector('[name=import-body]');if(navigator.clipboard?.readText)navigator.clipboard.readText().then(text=>{target.value=text;notify('内容已粘贴');}).catch(()=>{messages.failure.title='读取剪贴板失败';messages.failure.text='当前预览不能读取剪贴板，请直接粘贴报告正文。';messages.failure.detail='';showFull('failure');});else notify('请在报告正文中直接粘贴');},
    'manual-history':()=>openPicker(false),
    'clear-draft':()=>showFull('confirm-clear'),
    'main-calendar':()=>showFull('calendar'),
    'pick-todo':()=>openPicker(true),
    'pick-history':()=>openPicker(false),
    'picker-calendar':()=>showFull('calendar'),
    'picker-all':()=>{const search=root.querySelector('[data-f-pick-search]');if(search)search.value='';root.querySelectorAll('[data-f-pick-row]').forEach(r=>r.hidden=false);const count=root.querySelector('[data-f-pick-count]');if(count)count.textContent=records.length+' 项';},
    'accept-pick':()=>{if(full.selected===null)return;const todo=full.screen==='picker-todo';const item=(todo?tasks:records).find(x=>String(x.id)===String(full.selected));if(!item)return;full.note=item.text;full.tag=item.tag;full.saved=false;const target=full.stack.pop()||'reminder-default';if(target==='main-manual'){state.drafts['manual:manual-content']=item.text;state.drafts['manual:manual-tag']=item.tag;showFull(target,false);}else showFull(todo?'reminder-todo':target,false);notify(todo?'内容和标签已填入草稿，待办状态未改变':'已带入历史记录，可继续填写');},
    'copy-picked':()=>{const item=records.find(x=>String(x.id)===String(full.selected));if(item)fCopy(item.text);else notify('请先选择一条记录');},
    'calendar-day':arg=>{const target=full.stack.pop()||'main-history';showFull(target,false);if(target==='main-history'){root.querySelector('[data-history-date]').value=arg;renderRecords();}else if(target==='picker-history'){root.querySelectorAll('[data-f-pick-row]').forEach(row=>row.hidden=row.dataset.date!==arg);const count=root.querySelector('[data-f-pick-count]');if(count)count.textContent=records.filter(r=>r.date===arg).length+' 项';}notify('已选择 '+arg);},
    'calendar-prev':()=>{full.calendarMonth='2026-09';render();},
    'calendar-next':()=>{full.calendarMonth='2026-10';render();},
    'toggle-tag':arg=>{captureFull();full.tag=full.tag===arg?'':arg;render();},
    'toggle-logs':()=>{captureFull();full.logsVisible=!full.logsVisible;if(full.screen==='reminder-logs'&&!full.logsVisible)full.screen='reminder-default';render();},
    'save-reminder':()=>{captureFull();if(!full.note.trim()){showFull('reminder-empty',false);notify('先写一点工作内容，再保存记录');return;}records.unshift({id:Date.now(),date:TODAY,start:'15:00',end:'15:45',text:full.note.trim(),tag:full.tag||'记录'});full.note='';full.saved=true;full.screen='reminder-saved';render();notify('示例工作记录已保存');},
    'continue-record':arg=>{const item=records.find(r=>String(r.id)===arg);if(item){full.note=item.text;full.tag=item.tag;full.saved=false;render();notify('已带入这项工作，可以继续填写');}},
    'edit-record':arg=>{full.selectedLog=Number(arg);showFull('edit-related');},
    'copy-record':arg=>{const item=records.find(r=>String(r.id)===arg);if(item)fCopy(item.text);},
    'delete-record':arg=>{full.selectedLog=Number(arg);showFull('confirm-delete-log');},
    'release-reminder':()=>showFull('access'),
    'copy-draft':()=>fCopy(full.note||'确认新版本的界面布局与交互流程，补充缩放与对齐检查项。'),
    'restore-reminder':()=>{full.stack=[];showFull('reminder-default',false);},
    'finish-break':()=>{full.stack=[];full.saved=false;showFull('main-reminder',false);notify('活动已完成（演示）');},
    'skip-break':()=>{full.stack=[];showFull('main-reminder',false);notify('已跳过本次提醒（演示）');},
    'postpone-break':()=>{full.stack=[];showFull('main-reminder',false);notify('已推迟 5 分钟（演示）');},
    'save-edited':arg=>{const text=root.querySelector('[data-f-edit-content]').value.trim();const error=root.querySelector('[data-f-edit-error]');if(!text){error.hidden=false;error.textContent=arg==='todo'?'待办内容不能为空。':'工作内容不能为空。';return;}const item=arg==='todo'?tasks.find(t=>t.id===full.selectedTask):records.find(r=>r.id===full.selectedLog);if(arg==='log'){const start=root.querySelector('[data-f-edit-start]').value,end=root.querySelector('[data-f-edit-end]').value;if(start>=end){error.hidden=false;error.textContent='结束时间需要晚于开始时间。';return;}if(item){item.date=root.querySelector('[data-f-edit-date]').value;item.start=start;item.end=end;}}if(item){item.text=text;item.tag=root.querySelector('[list=tn-all-tags]').value;}backFull();notify('示例内容已保存');},
    'wizard-folder':()=>showFull('native-folder'),
    'wizard-start':()=>{const custom=root.querySelector('[name=full-storage]:checked')?.value==='custom';const path=root.querySelector('[name=wizard-path]').value.trim();if(custom&&!path){notify('请选择或填写数据目录');return;}full.stack=[];showFull('main-todos',false);notify('开始使用（演示，不创建文件或目录）');},
    'native-select':()=>{},
    'native-accept':arg=>{const previous=full.stack.pop()||'main-export';showFull(previous,false);if(arg==='folder'){if(previous.startsWith('wizard'))showFull('wizard-custom',false);else showFull('confirm-migrate');}else{messages.success.title='导出成功';messages.success.text='示例工作流水已准备导出。';messages.success.detail='D:\\文档\\工作流水_2026-10-26_2026-10-30.md';showFull('success');}},
    'tray-page':arg=>{full.stack=[];showFull('main-'+arg,false);},
    'tray-copy':arg=>{full.stack=[];showFull('balloon',false);notify(arg==='weekly'?'周报提示词已复制（演示）':'日报提示词已复制（演示）');},
    'tray-pause':()=>showFull('tray-pause',false),
    'tray-resume':()=>{state.paused=false;showFull('tray-counting',false);notify('提醒已恢复（演示）');},
    'tray-exit':()=>showFull('confirm-unsaved'),
    'pause-time':arg=>{state.paused=true;showFull('main-reminder');notify(arg==='today'?'已暂停到今天下班（演示）':`已暂停 ${arg} 分钟（演示）`);},
    'confirm-clear':()=>{const field=root.querySelector('[name=manual-content]');if(field)field.value='';delete state.drafts['manual:manual-content'];backFull();notify('草稿已清空（演示）');},
    'confirm-delete':arg=>{if(arg==='todo')tasks=tasks.filter(t=>t.id!==full.selectedTask);if(arg==='log')records=records.filter(r=>r.id!==full.selectedLog);if(arg==='report'){const index=reports.findIndex(r=>r.id===state.reportId);if(index>=0)reports.splice(index,1);}backFull();notify('示例内容已删除');},
    'import-new':()=>completeImport(false),
    'import-overwrite':()=>completeImport(true),
    'simulate-migrate':()=>{full.stack=[];showFull('settings-data',false);messages.success.title='数据位置已更换';messages.success.text='迁移完成后，软件会自动重新启动。';messages.success.detail='当前是界面预览，不移动文件或重新启动程序。';showFull('success');},
    discard:()=>{backFull();notify('已放弃修改并继续（演示）');},
    ack:()=>{backFull();notify('已保存并继续（演示）');},
    'copy-long':()=>fCopy(root.querySelector('.tn-window-body textarea').value)
  };
  root.addEventListener('click',event=>{const button=event.target.closest('button[data-f-action]');if(!button)return;if(button.dataset.fAction==='native-select'){root.querySelectorAll('.tn-native-file').forEach(el=>el.setAttribute('aria-selected',String(el===button)));return;}faction[button.dataset.fAction]?.(button.dataset.fArg||'');});
  root.addEventListener('input',event=>{if(event.target.matches('[data-f-note]')){full.note=event.target.value;root.querySelectorAll('[data-f-note]').forEach(el=>{if(el!==event.target)el.value=full.note;});}if(event.target.matches('[data-f-pick-search]'))filterPicker();});
  root.addEventListener('change',event=>{
    if(event.target.name==='full-pick'){full.selected=event.target.value;const button=root.querySelector('[data-f-action=accept-pick]');if(button)button.disabled=false;const item=records.find(r=>String(r.id)===String(full.selected)),preview=root.querySelector('[data-f-pick-preview]');if(preview)preview.value=item?.text||'';}
    if(event.target.matches('[data-f-pick-date]'))filterPicker();
    if(event.target.matches('[data-f-month]')){full.calendarMonth=event.target.value;render();}
    if(event.target.name==='full-storage')showFull(event.target.value==='custom'?'wizard-custom':'wizard',false);
  });
  root.addEventListener('submit',event=>{if(event.target.dataset.form==='import'){const data=new FormData(event.target),type=data.get('import-type'),date=data.get('import-date'),body=data.get('import-body').trim();if(body&&reports.some(r=>r.type===type&&r.date===date)){event.preventDefault();event.stopImmediatePropagation();full.pendingImport={type,date,body};showFull('confirm-overwrite');}}},true);
  root.addEventListener('submit',event=>{if(event.target.dataset.form==='settings'){captureDraft();full.savedSettings=Object.fromEntries(Object.entries(state.drafts).filter(([key])=>key.startsWith('settings:')));full.savedTags=[...full.tags];}});
  root.addEventListener('keydown',event=>{if(event.key==='Escape'&&root.dataset.fullMode==='extra'){event.preventDefault();if(full.screen.startsWith('reminder-'))faction['postpone-break']();else backFull();}});
