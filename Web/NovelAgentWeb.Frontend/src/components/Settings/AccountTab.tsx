import CollapsePanel from './CollapsePanel';
import type { UserSettings } from '../../api/types';
import { useAuthStore } from '../../stores/authStore';

interface AccountTabProps {
  form: Partial<UserSettings>;
  update: (_key: keyof UserSettings, _value: unknown) => void;
}

export default function AccountTab({ form: _form, update: _update }: AccountTabProps) {
  const { user } = useAuthStore();
  const isAdmin = user?.role === 'admin';

  return (
    <div className="settings-tab-content">
      {/* 1.1 Personal Info */}
      <CollapsePanel id="account-1" title="个人信息" defaultOpen={true}>
        <div className="form-grid">
          <div className="form-field">
            <label>用户名</label>
            <input value={user?.username || ''} disabled />
          </div>
          <div className="form-field">
            <label>邮箱</label>
            <input value={user?.email || ''} disabled />
          </div>
          <div className="form-field">
            <label>角色</label>
            <input value={user?.role || 'user'} disabled />
          </div>
        </div>
      </CollapsePanel>

      {/* 1.2 Security Settings */}
      <CollapsePanel id="account-2" title="安全设置" defaultOpen={false}>
        <div className="form-grid">
          <div className="form-field">
            <label>修改密码</label>
            <p className="settings-desc" style={{ marginBottom: 8 }}>密码修改功能即将推出</p>
            <button className="ghost-button" disabled>修改密码</button>
          </div>
          <div className="form-field">
            <label>
              <input
                type="checkbox"
                checked={false}
                disabled
              />
              启用两步验证（即将推出）
            </label>
          </div>
        </div>
      </CollapsePanel>

      {/* 1.3 Usage Stats */}
      <CollapsePanel id="account-3" title="使用统计" defaultOpen={false}>
        <div className="form-grid">
          <div className="form-field">
            <label>存储空间使用</label>
            <div className="usage-bar">
              <div className="usage-fill" style={{ width: '0%' }}></div>
            </div>
            <p className="usage-text">0 MB / 1000 MB</p>
          </div>
          <div className="form-field">
            <label>本月 API 调用次数</label>
            <p className="usage-text">0 / 10000</p>
          </div>
        </div>
      </CollapsePanel>

      {/* 1.4 Quota Management (Admin only) */}
      {isAdmin && (
        <CollapsePanel id="account-4" title="配额管理 [管理员]" defaultOpen={false}>
          <div className="form-grid">
            <div className="form-field">
              <label>管理用户配额</label>
              <p className="settings-desc" style={{ marginBottom: 8 }}>配额管理功能即将推出</p>
              <button className="ghost-button" disabled>配置配额</button>
            </div>
          </div>
        </CollapsePanel>
      )}
    </div>
  );
}
