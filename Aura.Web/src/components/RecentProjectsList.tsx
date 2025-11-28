/**
 * Component to display recent wizard projects
 */

import {
  Card,
  CardHeader,
  Button,
  Spinner,
  Text,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Toast,
  ToastTitle,
  useToastController,
} from '@fluentui/react-components';
import {
  MoreVertical20Regular,
  Open20Regular,
  Copy20Regular,
  Delete20Regular,
  ArrowDownload20Regular,
} from '@fluentui/react-icons';
import { useEffect, useState, useCallback } from 'react';
import type { FC } from 'react';
import {
  getRecentWizardProjects,
  deleteWizardProject,
  duplicateWizardProject,
  exportWizardProject,
  downloadProjectExport,
} from '../api/wizardProjects';
import { useWizardProjectStore } from '../state/wizardProject';
import type { WizardProjectListItem } from '../types/wizardProject';

interface RecentProjectsListProps {
  onOpenProject?: (project: WizardProjectListItem) => void;
  maxItems?: number;
}

const RecentProjectsList: FC<RecentProjectsListProps> = ({ onOpenProject, maxItems = 5 }) => {
  const { projectList, setProjectList } = useWizardProjectStore();
  const { dispatchToast } = useToastController('global');
  const [isLoading, setIsLoading] = useState(false);
  const [actionInProgress, setActionInProgress] = useState<string | null>(null);

  const loadProjects = useCallback(async () => {
    setIsLoading(true);
    try {
      const projects = await getRecentWizardProjects(maxItems);
      setProjectList(projects);
    } catch (error) {
      console.error('Failed to load recent projects:', error);
      dispatchToast(
        <Toast>
          <ToastTitle>Failed to load recent projects</ToastTitle>
        </Toast>,
        { intent: 'error', timeout: 5000 }
      );
    } finally {
      setIsLoading(false);
    }
  }, [maxItems, setProjectList, dispatchToast]);

  useEffect(() => {
    loadProjects();
  }, [loadProjects]);

  const handleOpenProject = useCallback(
    (project: WizardProjectListItem) => {
      if (onOpenProject) {
        onOpenProject(project);
      }
    },
    [onOpenProject]
  );

  const handleDuplicate = useCallback(
    async (project: WizardProjectListItem) => {
      setActionInProgress(project.id);
      try {
        await duplicateWizardProject(project.id, {
          newName: `${project.name} (Copy)`,
        });
        dispatchToast(
          <Toast>
            <ToastTitle>Project duplicated successfully</ToastTitle>
          </Toast>,
          { intent: 'success', timeout: 5000 }
        );
        loadProjects();
      } catch (error) {
        console.error('Failed to duplicate project:', error);
        dispatchToast(
          <Toast>
            <ToastTitle>Failed to duplicate project</ToastTitle>
          </Toast>,
          { intent: 'error', timeout: 5000 }
        );
      } finally {
        setActionInProgress(null);
      }
    },
    [dispatchToast, loadProjects]
  );

  const handleExport = useCallback(
    async (project: WizardProjectListItem) => {
      setActionInProgress(project.id);
      try {
        const projectJson = await exportWizardProject(project.id);
        downloadProjectExport(projectJson, project.name);
        dispatchToast(
          <Toast>
            <ToastTitle>Project exported successfully</ToastTitle>
          </Toast>,
          { intent: 'success', timeout: 5000 }
        );
      } catch (error) {
        console.error('Failed to export project:', error);
        dispatchToast(
          <Toast>
            <ToastTitle>Failed to export project</ToastTitle>
          </Toast>,
          { intent: 'error', timeout: 5000 }
        );
      } finally {
        setActionInProgress(null);
      }
    },
    [dispatchToast]
  );

  const handleDelete = useCallback(
    async (project: WizardProjectListItem) => {
      if (!confirm(`Are you sure you want to delete "${project.name}"?`)) {
        return;
      }

      setActionInProgress(project.id);
      try {
        await deleteWizardProject(project.id);
        dispatchToast(
          <Toast>
            <ToastTitle>Project deleted successfully</ToastTitle>
          </Toast>,
          { intent: 'success', timeout: 5000 }
        );
        loadProjects();
      } catch (error) {
        console.error('Failed to delete project:', error);
        dispatchToast(
          <Toast>
            <ToastTitle>Failed to delete project</ToastTitle>
          </Toast>,
          { intent: 'error', timeout: 5000 }
        );
      } finally {
        setActionInProgress(null);
      }
    },
    [dispatchToast, loadProjects]
  );

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleDateString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  if (isLoading) {
    return (
      <div style={{ padding: '20px', textAlign: 'center' }}>
        <Spinner label="Loading recent projects..." />
      </div>
    );
  }

  if (projectList.length === 0) {
    return (
      <div style={{ padding: '20px', textAlign: 'center' }}>
        <Text>No recent projects found. Create your first project to get started!</Text>
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
      {projectList.map((project: WizardProjectListItem) => (
        <Card key={project.id}>
          <CardHeader
            header={<Text weight="semibold">{project.name}</Text>}
            description={
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                {project.description && <Text size={200}>{project.description}</Text>}
                <Text size={200} style={{ color: 'var(--colorNeutralForeground3)' }}>
                  Last modified: {formatDate(project.updatedAt)} • Step {project.currentStep + 1} •{' '}
                  {project.progressPercent}%
                </Text>
              </div>
            }
            action={
              <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                <Button
                  appearance="primary"
                  icon={<Open20Regular />}
                  onClick={() => handleOpenProject(project)}
                  disabled={actionInProgress === project.id}
                >
                  Open
                </Button>
                <Menu>
                  <MenuTrigger disableButtonEnhancement>
                    <Button
                      appearance="subtle"
                      icon={<MoreVertical20Regular />}
                      disabled={actionInProgress === project.id}
                    />
                  </MenuTrigger>
                  <MenuPopover>
                    <MenuList>
                      <MenuItem icon={<Copy20Regular />} onClick={() => handleDuplicate(project)}>
                        Duplicate
                      </MenuItem>
                      <MenuItem
                        icon={<ArrowDownload20Regular />}
                        onClick={() => handleExport(project)}
                      >
                        Export
                      </MenuItem>
                      <MenuItem icon={<Delete20Regular />} onClick={() => handleDelete(project)}>
                        Delete
                      </MenuItem>
                    </MenuList>
                  </MenuPopover>
                </Menu>
              </div>
            }
          />
        </Card>
      ))}
    </div>
  );
};

export default RecentProjectsList;
