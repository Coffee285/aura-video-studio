/**
 * Dialog for saving wizard projects
 */

import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Input,
  Label,
  Textarea,
  Spinner,
  Toast,
  ToastTitle,
  useToastController,
  useId,
} from '@fluentui/react-components';
import { Save20Regular, Dismiss20Regular } from '@fluentui/react-icons';
import { useState, useCallback, useEffect } from 'react';
import type { FC } from 'react';
import { saveWizardProject } from '../api/wizardProjects';
import { useWizardProjectStore, generateDefaultProjectName } from '../state/wizardProject';
import type { SaveWizardProjectRequest } from '../types/wizardProject';

interface SaveProjectDialogProps {
  isOpen: boolean;
  onClose: () => void;
  currentStep: number;
  briefData?: unknown;
  planData?: unknown;
  voiceData?: unknown;
  renderData?: unknown;
  onSaveSuccess?: (projectId: string) => void;
}

const SaveProjectDialog: FC<SaveProjectDialogProps> = ({
  isOpen,
  onClose,
  currentStep,
  briefData,
  planData,
  voiceData,
  renderData,
  onSaveSuccess,
}) => {
  const { currentProject, setSaving, setCurrentProject, setLastSaveTime } = useWizardProjectStore();
  const { dispatchToast } = useToastController('global');
  const nameInputId = useId('project-name-input');
  const descriptionInputId = useId('project-description-input');

  const [name, setName] = useState(currentProject?.name || '');
  const [description, setDescription] = useState(currentProject?.description || '');
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (isOpen && !currentProject) {
      setName(generateDefaultProjectName());
    } else if (isOpen && currentProject) {
      setName(currentProject.name);
      setDescription(currentProject.description || '');
    }
  }, [isOpen, currentProject]);

  const handleSave = useCallback(async () => {
    if (!name.trim()) {
      dispatchToast(
        <Toast>
          <ToastTitle>Project name is required</ToastTitle>
        </Toast>,
        { intent: 'error', timeout: 5000 }
      );
      return;
    }

    setIsSaving(true);
    setSaving(true);

    try {
      const request: SaveWizardProjectRequest = {
        id: currentProject?.id,
        name: name.trim(),
        description: description.trim() || undefined,
        currentStep,
        briefJson: briefData ? JSON.stringify(briefData) : undefined,
        planSpecJson: planData ? JSON.stringify(planData) : undefined,
        voiceSpecJson: voiceData ? JSON.stringify(voiceData) : undefined,
        renderSpecJson: renderData ? JSON.stringify(renderData) : undefined,
      };

      const response = await saveWizardProject(request);

      if (currentProject) {
        setCurrentProject({
          ...currentProject,
          id: response.id,
          name: response.name,
          updatedAt: response.lastModifiedAt,
        });
      } else {
        setCurrentProject({
          id: response.id,
          name: response.name,
          description: description.trim() || undefined,
          status: 'Draft',
          progressPercent: 0,
          currentStep,
          createdAt: response.lastModifiedAt,
          updatedAt: response.lastModifiedAt,
          briefJson: request.briefJson,
          planSpecJson: request.planSpecJson,
          voiceSpecJson: request.voiceSpecJson,
          renderSpecJson: request.renderSpecJson,
          generatedAssets: [],
        });
      }

      setLastSaveTime(new Date());

      dispatchToast(
        <Toast>
          <ToastTitle>Project saved successfully</ToastTitle>
        </Toast>,
        { intent: 'success', timeout: 5000 }
      );

      if (onSaveSuccess) {
        onSaveSuccess(response.id);
      }

      onClose();
    } catch (error) {
      console.error('Failed to save project:', error);
      dispatchToast(
        <Toast>
          <ToastTitle>Failed to save project</ToastTitle>
        </Toast>,
        { intent: 'error', timeout: 5000 }
      );
    } finally {
      setIsSaving(false);
      setSaving(false);
    }
  }, [
    name,
    description,
    currentStep,
    briefData,
    planData,
    voiceData,
    renderData,
    currentProject,
    setSaving,
    setCurrentProject,
    setLastSaveTime,
    dispatchToast,
    onSaveSuccess,
    onClose,
  ]);

  const handleGenerateName = useCallback(() => {
    setName(generateDefaultProjectName());
  }, []);

  return (
    <Dialog open={isOpen} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>Save Project</DialogTitle>
          <DialogContent>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
              <div>
                <Label htmlFor={nameInputId} required>
                  Project Name
                </Label>
                <div style={{ display: 'flex', gap: '8px', marginTop: '4px' }}>
                  <Input
                    id={nameInputId}
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="Enter project name"
                    disabled={isSaving}
                    style={{ flex: 1 }}
                  />
                  <Button appearance="secondary" onClick={handleGenerateName} disabled={isSaving}>
                    Auto-generate
                  </Button>
                </div>
              </div>

              <div>
                <Label htmlFor={descriptionInputId}>Description (optional)</Label>
                <Textarea
                  id={descriptionInputId}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Enter project description"
                  disabled={isSaving}
                  rows={3}
                  style={{ marginTop: '4px' }}
                />
              </div>
            </div>
          </DialogContent>
          <DialogActions>
            <Button
              appearance="secondary"
              onClick={onClose}
              disabled={isSaving}
              icon={<Dismiss20Regular />}
            >
              Cancel
            </Button>
            <Button
              appearance="primary"
              onClick={handleSave}
              disabled={isSaving || !name.trim()}
              icon={isSaving ? <Spinner size="tiny" /> : <Save20Regular />}
            >
              {isSaving ? 'Saving...' : 'Save Project'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};

export default SaveProjectDialog;
