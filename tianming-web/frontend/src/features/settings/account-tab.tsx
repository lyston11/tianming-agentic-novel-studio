import { useAuth } from '@/features/auth/auth-context-value';
import {
  SettingsSection,
  SettingsField,
  SettingsHint,
} from './settings-section';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export function AccountTab() {
  const { user } = useAuth();
  const isAdmin = user?.role === 'admin';

  return (
    <div className="space-y-4">
      <SettingsSection title="个人信息" defaultOpen>
        <SettingsField label="用户名">
          <Input value={user?.username ?? ''} disabled />
        </SettingsField>
        <SettingsField label="邮箱">
          <Input value={user?.email ?? ''} disabled />
        </SettingsField>
        <SettingsField label="角色">
          <Input value={user?.role ?? 'user'} disabled />
        </SettingsField>
      </SettingsSection>

      <SettingsSection title="安全设置">
        <SettingsField label="修改密码">
          <SettingsHint>密码修改功能即将推出</SettingsHint>
          <Button variant="outline" size="sm" disabled>修改密码</Button>
        </SettingsField>
        <SettingsHint>两步验证（即将推出）</SettingsHint>
      </SettingsSection>

      <SettingsSection title="使用统计">
        <SettingsField label="存储空间使用">
          <SettingsHint>0 MB / 1000 MB</SettingsHint>
        </SettingsField>
        <SettingsField label="本月 API 调用次数">
          <SettingsHint>0 / 10000</SettingsHint>
        </SettingsField>
      </SettingsSection>

      {isAdmin && (
        <SettingsSection title="配额管理（管理员）">
          <SettingsHint>配额管理功能即将推出</SettingsHint>
          <Button variant="outline" size="sm" disabled>配置配额</Button>
        </SettingsSection>
      )}
    </div>
  );
}
